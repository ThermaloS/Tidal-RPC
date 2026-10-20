using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TidalRpc;

internal static partial class Program
{
    private const string Usage = """
        TIDAL RPC for macOS: share your TIDAL playback on Discord.

        Usage: tidal-rpc <command>
          install            Start now and at every login (runs in the background)
          uninstall          Stop and remove from login
          run                Run in this terminal until Ctrl+C
          settings           Show settings
          set <name> <value> Change a setting; a running app picks it up within seconds
                               sharing  on|off
                               country  two-letter code, such as US or GB
                               artwork  on|off
                               timer    on|off
                               button   on|off
                               status   artist|song|app
          version            Show the version
        """;

    private static int Main(string[] args)
    {
        try
        {
            // Opening TIDAL RPC from Finder passes no arguments; that means install and start, like `install` in Terminal.
            if (args.Length == 0 && IsAppBundle()) return InstallFromApp();
            switch (args.FirstOrDefault())
            {
                case "run": return Run();
                case "install":
                    if (File.Exists(MacSettingsStore.FilePath)) MacSettingsStore.Load();
                    else MacSettingsStore.Save(new());
                    LaunchAgent.Install();
                    Console.WriteLine("TIDAL RPC is running and will start at login. Play a song in TIDAL to share it on Discord.");
                    return 0;
                case "uninstall":
                    LaunchAgent.Uninstall();
                    Console.WriteLine("TIDAL RPC stopped and won't start at login.");
                    return 0;
                case "settings":
                    Console.WriteLine(MacSettingsStore.Serialize(MacSettingsStore.Load()));
                    Console.WriteLine($"Saved in {MacSettingsStore.FilePath}");
                    Console.WriteLine(LaunchAgent.Installed ? "Starts at login." : "Doesn't start at login. Run: tidal-rpc install");
                    return 0;
                case "set" when args.Length == 3:
                    MacSettingsStore.Save(Set(MacSettingsStore.Load(), args[1], args[2]));
                    Console.WriteLine("Saved.");
                    return 0;
                case "version": Console.WriteLine($"TIDAL RPC {ReleaseConfiguration.Version}"); return 0;
                default: Console.WriteLine(Usage); return args.Length == 0 || args[0] is "help" or "--help" or "-h" ? 0 : 64;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine(ex is System.Text.Json.JsonException ? $"Couldn't read {MacSettingsStore.FilePath}." : ex.Message);
            return 1;
        }
    }

    private static bool IsAppBundle() => Environment.ProcessPath?.Contains(".app/Contents/MacOS/", StringComparison.Ordinal) == true;

    private static int InstallFromApp()
    {
        try
        {
            if (!File.Exists(MacSettingsStore.FilePath)) MacSettingsStore.Save(new());
            LaunchAgent.Install();
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            ShowAlert(ex.Message);
            return 1;
        }
    }

    // Finder launches have no Terminal to print to, so errors go in a dialog.
    private static void ShowAlert(string message)
    {
        using var osascript = Process.Start(new ProcessStartInfo("/usr/bin/osascript")
        {
            UseShellExecute = false,
            ArgumentList = { "-e", "on run argv", "-e", "display dialog (item 1 of argv) with title \"TIDAL RPC\" buttons {\"OK\"} default button 1", "-e", "end run", message }
        });
        osascript?.WaitForExit();
    }

    public static AppSettings Set(AppSettings s, string name, string value)
    {
        bool Switch() => value.ToLowerInvariant() switch
        {
            "on" or "true" or "yes" => true,
            "off" or "false" or "no" => false,
            _ => throw new InvalidOperationException($"Use on or off for {name}.")
        };
        return name.ToLowerInvariant() switch
        {
            "sharing" => s with { Enabled = Switch() },
            "country" => s with { Country = value.ToUpperInvariant() },
            "artwork" => s with { ShowArtwork = Switch() },
            "timer" => s with { ShowPlaybackTime = Switch() },
            "button" => s with { ShowTidalButton = Switch() },
            "status" => s with { StatusText = value.ToLowerInvariant() switch
            {
                "artist" => StatusTextMode.Artist, "song" => StatusTextMode.Song, "app" => StatusTextMode.AppName,
                _ => throw new InvalidOperationException("Use artist, song or app for status.")
            } },
            _ => throw new InvalidOperationException($"Unknown setting: {name}. Run tidal-rpc help.")
        };
    }

    private static int Run()
    {
        Directory.CreateDirectory(MacSettingsStore.Folder);
        FileStream instance;
        // .NET takes an exclusive flock for FileShare.None, released when the process exits.
        try { instance = new FileStream(Path.Combine(MacSettingsStore.Folder, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { Console.Error.WriteLine("TIDAL RPC is already running."); return 1; }
        using (instance)
        {
            UseUserTempDirectory();
            var loop = new EventLoop();
            SynchronizationContext.SetSynchronizationContext(loop);
            using var stop = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => { context.Cancel = true; loop.Complete(); });
            using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, context => { context.Cancel = true; loop.Complete(); });
            using var host = new MacHost(loop);
            loop.Post(_ => host.Start(), null);
            loop.Run(MenuBar.Pump);
        }
        return 0;
    }

    // Discord listens on $TMPDIR/discord-ipc-N. launchd agents may start without TMPDIR,
    // so use the same per-user folder Discord sees.
    private static void UseUserTempDirectory()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TMPDIR")) || !OperatingSystem.IsMacOS()) return;
        var buffer = new byte[1024];
        var length = confstr(65537 /* _CS_DARWIN_USER_TEMP_DIR */, buffer, (nuint)buffer.Length);
        if (length > 1 && length <= (nuint)buffer.Length)
            Environment.SetEnvironmentVariable("TMPDIR", System.Text.Encoding.UTF8.GetString(buffer, 0, (int)length - 1));
    }

    [LibraryImport("libc")]
    private static partial nuint confstr(int name, [Out] byte[] buffer, nuint length);
}

// One thread runs every callback, matching the single UI thread the shared controller expects.
internal sealed class EventLoop : SynchronizationContext
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> queue = [];
    public override void Post(SendOrPostCallback d, object? state)
    {
        try { queue.Add((d, state)); } catch (InvalidOperationException) { }
    }
    public override SynchronizationContext CreateCopy() => this;
    public void Complete() => queue.CompleteAdding();
    // The menu bar needs AppKit events pumped on this same thread, so when the queue is empty the loop
    // calls whenIdle (which waits briefly for AppKit events) instead of blocking on the queue.
    public void Run(Action whenIdle)
    {
        while (true)
        {
            while (queue.TryTake(out var item))
            {
                try { item.Callback(item.State); }
                catch (Exception ex) { MacHost.Log($"Unexpected error: {ex.GetType().Name}"); }
            }
            if (queue.IsCompleted) break;
            whenIdle();
        }
    }
}

internal sealed class MacHost(EventLoop context) : IDisposable
{
    private readonly MacPlaybackProvider playback = new();
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly CancellationTokenSource updateLifetime = new();
    private AppSettings settings = new();
    private DateTime settingsStamp;
    private DiscordPresencePublisher? publisher;
    private PresenceController? controller;
    private PlaybackSnapshot? latest;
    private Timer? timer;
    private int ticks;
    private bool disposed;
    private DateTimeOffset nextUpdateCheck = DateTimeOffset.UtcNow;
    private Version? notifiedVersion;
    private string? discordStatus = "", playbackStatus = "";
    // Problems shown in the menu bar dropdown. Null means nothing to report.
    private string? discordIssue, mediaIssue;
    private MenuBar? menu;
    private static string? lastLog;

    // Status only: song titles and artists stay out of the log.
    public static void Log(string text)
    {
        if (Interlocked.Exchange(ref lastLog, text) != text) Console.Error.WriteLine($"{DateTime.Now:HH:mm:ss} {text}");
    }

    public void Start()
    {
        LoadSettings();
        Configure();
        playback.Changed += track =>
        {
            if (latest?.Key != track?.Key || latest?.Status != track?.Status)
                DebugConsole.Write("Playback", track is null ? "TIDAL session removed" : $"{track.Status}: {track.Title} — {track.Artist} | {track.Album}");
            mediaIssue = null;
            latest = track; controller?.Update(track);
        };
        playback.Diagnostic += text => { DebugConsole.Write("Playback", text); Log(text); mediaIssue = text; };
        _ = playback.StartAsync();
        menu = new MenuBar(MenuState, ReportProblem, context.Complete, RemoveFromLogin);
        timer = new Timer(_ => context.Post(_ => Tick(), null), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        Log($"TIDAL RPC {ReleaseConfiguration.Version} started");
    }

    private void Tick()
    {
        if (disposed) return;
        controller?.Tick();
        if (++ticks % 5 == 0 && MacSettingsStore.Stamp != settingsStamp)
        {
            var previous = settings;
            LoadSettings();
            Apply(previous);
        }
        if (DateTimeOffset.UtcNow >= nextUpdateCheck) _ = CheckForUpdatesAsync();
    }

    private void LoadSettings()
    {
        settingsStamp = MacSettingsStore.Stamp;
        try { settings = MacSettingsStore.Load(); }
        catch (Exception ex)
        {
            settings = settings with { Enabled = false };
            DebugConsole.Write("Settings", $"Load failed: {ex.GetType().Name}");
            Log($"Couldn't read {MacSettingsStore.FilePath}. Sharing is paused until it's fixed.");
        }
    }

    private void Configure()
    {
        controller?.Dispose(); publisher?.Dispose();
        discordIssue = null;
        publisher = new(settings.DiscordApplicationId);
        publisher.SetSettings(settings);
        var active = publisher;
        publisher.Status += text => context.Post(_ =>
        {
            if (disposed || !ReferenceEquals(publisher, active) || discordStatus == text) return;
            discordStatus = text; discordIssue = text; Log(text ?? "Discord connected");
        }, null);
        var endpoint = ReleaseConfiguration.MetadataService;
        if (endpoint is null) Log("This build isn't connected yet.");
        ITrackMetadataProvider metadata = endpoint is null ? new UnavailableMetadataProvider() : new HostedMetadataProvider(http, endpoint, settings.Country);
        controller = new(publisher, metadata);
        controller.Status += text => { if (playbackStatus != text) { playbackStatus = text; Log(text); } };
        controller.SetEnabled(settings.Enabled);
        publisher.Start(); controller.Update(latest);
    }

    private void Apply(AppSettings previous)
    {
        if (settings == previous) return;
        Log("Settings updated");
        if (settings.Country != previous.Country) { Configure(); return; }
        publisher?.SetSettings(settings);
        if (settings.Enabled != previous.Enabled) controller?.SetEnabled(settings.Enabled);
        else controller?.RefreshPresence();
    }

    private async Task CheckForUpdatesAsync()
    {
        nextUpdateCheck = DateTimeOffset.UtcNow.AddHours(24);
        try
        {
            var repository = ReleaseConfiguration.Repository ?? throw new InvalidOperationException();
            var version = await UpdateChecker.CheckAsync(repository, ReleaseConfiguration.Version, updateLifetime.Token);
            if (disposed || version is null || version == notifiedVersion) return;
            notifiedVersion = version;
            Log($"TIDAL RPC {version} is available: {new Uri(repository, "releases/latest")}");
            Notify($"TIDAL RPC {version} is available. Download it from GitHub.");
        }
        catch (Exception ex) { DebugConsole.Write("Updates", $"Check failed: {ex.GetType().Name}"); }
    }

    private static void Notify(string text)
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            using var process = Process.Start(new ProcessStartInfo("/usr/bin/osascript",
                ["-e", "on run argv", "-e", "display notification (item 1 of argv) with title \"TIDAL RPC\"", "-e", "end run", text]) { UseShellExecute = false });
        }
        catch (Exception ex) { DebugConsole.Write("Updates", $"Notification failed: {ex.GetType().Name}"); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        timer?.Dispose(); menu?.Dispose(); playback.Dispose(); controller?.Dispose(); publisher?.Dispose();
        updateLifetime.Cancel(); updateLifetime.Dispose(); http.Dispose();
        Log("TIDAL RPC stopped");
    }

    // Called each time the menu bar dropdown opens, so it always shows the current song and status.
    private (string NowPlaying, string Status) MenuState() => (NowPlayingLine(), StatusLine());

    private string NowPlayingLine() => latest switch
    {
        { Status: PlaybackStatus.Playing } track => Shorten($"Playing: {track.Title} — {track.Artist}"),
        { Status: PlaybackStatus.Paused } track => Shorten($"Paused: {track.Title} — {track.Artist}"),
        _ => "Nothing playing in TIDAL"
    };

    private string StatusLine()
    {
        if (!settings.Enabled) return "Sharing is off";
        if (discordIssue is not null) return discordIssue;
        if (mediaIssue is not null) return mediaIssue;
        if (playbackStatus is "This track is unavailable." or "Couldn't load this track. Retrying…") return playbackStatus;
        return "Sharing on Discord";
    }

    private static string Shorten(string text) => text.Length > 70 ? text[..69] + "…" : text;

    // Stops the background agent and removes it from login, so the app can be deleted without leaving it running.
    private void RemoveFromLogin()
    {
        try { LaunchAgent.Uninstall(); }
        catch (Exception ex) { Log($"Couldn't remove from login: {ex.GetType().Name}"); return; }
        context.Complete();
    }

    // Opens a prefilled GitHub issue with diagnostics. Song titles stay out unless the user adds them.
    private void ReportProblem()
    {
        try
        {
            var repository = ReleaseConfiguration.Repository ?? throw new InvalidOperationException("No repository is configured.");
            var report = IssueReport.Create(settings, latest, DebugConsole.Snapshot().Text);
            using (var pbcopy = Process.Start(new ProcessStartInfo("/usr/bin/pbcopy") { RedirectStandardInput = true, UseShellExecute = false })!)
            {
                pbcopy.StandardInput.Write(report);
                pbcopy.StandardInput.Close();
                pbcopy.WaitForExit();
            }
            var url = IssueReport.CreateIssueUri(repository, report);
            using var open = Process.Start(new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false, ArgumentList = { url.AbsoluteUri } });
        }
        catch (Exception ex) { Log($"Couldn't open the report: {ex.GetType().Name}"); }
    }
}
