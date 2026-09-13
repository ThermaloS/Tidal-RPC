using System.Text.Json;

namespace TidalRpc;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
#if DEBUG
        if (args.Contains("--diagnose")) { RunDiagnostic(); return; }
#endif
        using var mutex = new Mutex(true, "Local\\TidalRpc", out bool first);
        if (!first) { MessageBox.Show("TIDAL RPC is already running. Open Settings from its tray icon.", "TIDAL RPC"); return; }
        Application.Run(new TrayApplication());
    }
#if DEBUG
    private static void RunDiagnostic()
    {
        // Explicit diagnostic mode only; never reads or exports credentials.
        using var form = new Form { ShowInTaskbar = false, WindowState = FormWindowState.Minimized };
        form.Shown += async (_, _) =>
        {
            try
            {
                var manager = await Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                var results = new List<object>();
                foreach (var session in manager.GetSessions())
                {
                    var isTidal = WindowsPlaybackProvider.IsTidal(session.SourceAppUserModelId);
                    if (!isTidal) { results.Add(new { Source = session.SourceAppUserModelId, IsTidal = false }); continue; }
                    var media = await session.TryGetMediaPropertiesAsync();
                    var timeline = session.GetTimelineProperties();
                    results.Add(new { Source = session.SourceAppUserModelId, IsTidal = true, media.Title, media.Artist, media.AlbumTitle,
                        Status = session.GetPlaybackInfo().PlaybackStatus.ToString(), timeline.Position, timeline.StartTime, timeline.EndTime });
                }
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "diagnostic.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "diagnostic.json"), JsonSerializer.Serialize(new { Error = ex.GetType().Name, ex.HResult })); }
            finally { form.Close(); }
        };
        Application.Run(form);
    }
#endif
}

internal sealed class TrayApplication : ApplicationContext
{
    private AppSettings settings;
    private readonly NotifyIcon tray;
    private readonly ToolStripMenuItem enabledItem;
    private readonly WindowsPlaybackProvider playback = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly SynchronizationContext context = SynchronizationContext.Current!;
    private DiscordPresencePublisher? publisher;
    private PresenceController? controller;
    private SettingsForm? form;
    private PlaybackSnapshot? latest;
    private bool disposed;
    private string playbackStatus = "Waiting for TIDAL";
    private string? discordStatus = "Connecting to Discord…";
    private string? settingsError;
    private string? integrationError;
    private string? mediaError;
    public TrayApplication()
    {
        bool firstLaunch = !SettingsStore.Exists;
        try { settings = SettingsStore.Load(); }
        catch (Exception ex) { settings = new(Enabled: false); settingsError = "Couldn't load preferences. Change a setting to save them again."; DebugConsole.Write("Settings", $"Load failed: {ex.GetType().Name}"); }
        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings", null, (_, _) => ShowSettings());
        enabledItem = new ToolStripMenuItem("Share on Discord") { Checked = settings.Enabled, CheckOnClick = true };
        enabledItem.Click += (_, _) =>
        {
            try { Apply(settings with { Enabled = enabledItem.Checked }); }
            catch (Exception) { enabledItem.Checked = settings.Enabled; MessageBox.Show("Could not save settings.", "TIDAL RPC"); }
        };
        menu.Items.Add(enabledItem); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Report a problem", null, (_, _) => ShowReport()); menu.Items.Add("Exit", null, (_, _) => ExitThread());
        tray = new NotifyIcon { Text = "TIDAL RPC", Icon = AppBrand.Icon, ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowSettings();
        Configure();
        DebugConsole.Write("App", "Started; SEND confirms pipe write, ACK confirms Discord response, neither confirms profile rendering.");
        playback.Changed += track =>
        {
            if (latest?.Key != track?.Key || latest?.Status != track?.Status)
                DebugConsole.Write("Playback", track is null ? "TIDAL session removed" : $"{track.Status}: {track.Title} — {track.Artist} | {track.Album}");
            latest = track; controller?.Update(track);
        };
        playback.Diagnostic += text => { DebugConsole.Write("Playback", text); playbackStatus = "Reconnecting to TIDAL…"; UpdateStatus(); };
        timer.Tick += (_, _) =>
        {
            controller?.Tick();
            playback.Poll();
        };
        timer.Start();
        _ = StartPlaybackAsync();
        if (firstLaunch || settings.OpenSettingsOnLaunch || settingsError is not null) ShowSettings();
    }
    private async Task StartPlaybackAsync()
    {
        try { await playback.StartAsync(); }
        catch (Exception ex) { mediaError = "Restart TIDAL RPC to reconnect to your music."; DebugConsole.Write("Playback", $"Session access failed: {ex.GetType().Name}"); UpdateStatus(); }
    }
    private void Configure()
    {
        controller?.Dispose(); publisher?.Dispose();
        discordStatus = "Connecting to Discord…";
        integrationError = null;
        publisher = new(settings.DiscordApplicationId);
        publisher.SetSettings(settings);
        var activePublisher = publisher;
        publisher.Status += text => context.Post(_ => { if (!disposed && ReferenceEquals(publisher, activePublisher)) { discordStatus = text; UpdateStatus(); } }, null);
        var endpoint = ReleaseConfiguration.MetadataService;
        if (endpoint is null) integrationError = "This build isn't connected yet.";
        ITrackMetadataProvider metadata = endpoint is null ? new UnavailableMetadataProvider() : new HostedMetadataProvider(http, endpoint, settings.Country);
        controller = new(publisher, metadata);
        controller.Status += text => { playbackStatus = text; UpdateStatus(); };
        controller.SetEnabled(settings.Enabled);
        publisher.Start(); controller.Update(latest);
    }
    private void Apply(AppSettings value)
    {
        SettingsStore.Validate(value);
        SettingsStore.SaveWithStartup(value, settings);
        var previous = settings;
        settings = value;
        settingsError = null;
        enabledItem.Checked = value.Enabled;
        if (value.Country != previous.Country) Configure();
        else
        {
            publisher?.SetSettings(value);
            if (value.Enabled != previous.Enabled) controller?.SetEnabled(value.Enabled);
            else if (value.ShowArtwork != previous.ShowArtwork || value.ShowPlaybackTime != previous.ShowPlaybackTime
                || value.ShowTidalButton != previous.ShowTidalButton || value.StatusText != previous.StatusText) controller?.RefreshPresence();
        }
        form?.SetSettings(value);
        UpdateStatus();
    }
    private void UpdateStatus()
    {
        if (!disposed) form?.SetStatus(settingsError ?? (!settings.Enabled ? "Sharing is off" : mediaError ?? integrationError ?? discordStatus ?? playbackStatus));
    }
    private void ShowSettings()
    {
        if (form is { Visible: false }) { form.Dispose(); form = null; }
        if (form is null || form.IsDisposed) { form = new(settings); form.Saved += Apply; form.ReportRequested += ShowReport; }
        UpdateStatus(); form.Show(); form.WindowState = FormWindowState.Normal; form.Activate();
    }
    private void ShowReport() { using var report = new ReportForm(settings, latest); report.ShowDialog(form is { Visible: true } ? form : null); }
    protected override void ExitThreadCore() { Dispose(); base.ExitThreadCore(); }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true; timer.Stop(); timer.Dispose(); playback.Dispose(); controller?.Dispose(); publisher?.Dispose();
            tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); form?.Dispose(); http.Dispose();
        }
        base.Dispose(disposing);
    }
}
