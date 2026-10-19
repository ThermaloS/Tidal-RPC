using System.Diagnostics;
using System.Text.Json;

namespace TidalRpc;

// Reads macOS Now Playing through one long-lived osascript helper (NowPlaying.js) that
// reports changes only. Events are raised on the synchronization context that started it.
public sealed class MacPlaybackProvider : IPlaybackProvider
{
    private readonly SynchronizationContext context = SynchronizationContext.Current ?? new SynchronizationContext();
    private readonly CancellationTokenSource lifetime = new();
    private Process? helper;
    public event Action<PlaybackSnapshot?>? Changed;
    public event Action<string>? Diagnostic;
    public static bool IsTidal(string? id) => id is not null
        && (id.Equals("com.tidal.desktop", StringComparison.OrdinalIgnoreCase) || id.Equals("TIDAL", StringComparison.OrdinalIgnoreCase));

    public Task StartAsync()
    {
        _ = RunAsync(lifetime.Token);
        return Task.CompletedTask;
    }

    // Returns null when TIDAL isn't the Now Playing app; throws on lines that aren't snapshots.
    public static PlaybackSnapshot? Parse(string line)
    {
        using var json = JsonDocument.Parse(line);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
        string Text(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
        double? Number(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            && v.GetDouble() is var n && double.IsFinite(n) ? n : null;
        if (root.TryGetProperty("error", out _)) throw new NotSupportedException(Text("error"));
        var source = new[] { Text("app"), Text("parent"), Text("name") }.FirstOrDefault(IsTidal);
        if (source is null) return null;
        var status = root.TryGetProperty("playing", out var playing) && playing.ValueKind == JsonValueKind.True
            ? PlaybackStatus.Playing : PlaybackStatus.Paused;
        TimeSpan? duration = Number("duration") is > 0 and var d ? TimeSpan.FromSeconds(d) : null;
        TimeSpan? position = duration is not null && Number("position") is { } p ? TimeSpan.FromSeconds(p) : null;
        if (position is { } at && (at < TimeSpan.Zero || at > duration)) { duration = null; position = null; }
        return new(source, Text("title"), Text("artist"), Text("album"), status, position, duration);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        string script;
        using (var stream = typeof(MacPlaybackProvider).Assembly.GetManifestResourceStream("TidalRpc.NowPlaying.js")!)
        using (var reader = new StreamReader(stream)) script = await reader.ReadToEndAsync(cancellationToken);
        var backoff = TimeSpan.FromSeconds(1);
        while (!cancellationToken.IsCancellationRequested)
        {
            var started = DateTimeOffset.UtcNow;
            var planned = false;
            try
            {
                var start = new ProcessStartInfo("/usr/bin/osascript", ["-l", "JavaScript"])
                    { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false };
                using var process = Process.Start(start) ?? throw new InvalidOperationException();
                helper = process;
                await process.StandardInput.WriteAsync(script);
                process.StandardInput.Close();
                while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
                {
                    PlaybackSnapshot? snapshot;
                    try { snapshot = Parse(line); }
                    catch (NotSupportedException) { Report("Now Playing is unavailable on this version of macOS"); continue; }
                    catch (Exception) { continue; }
                    context.Post(_ => { if (!lifetime.IsCancellationRequested) Changed?.Invoke(snapshot); }, null);
                }
                await process.WaitForExitAsync(cancellationToken);
                planned = process.ExitCode == 0;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (Exception ex) { Report($"Now Playing helper failed: {ex.GetType().Name}"); }
            finally { helper = null; }
            // A planned restart keeps the current track; anything else drops it until the helper is back.
            if (planned) { backoff = TimeSpan.FromSeconds(1); continue; }
            context.Post(_ => { if (!lifetime.IsCancellationRequested) Changed?.Invoke(null); }, null);
            backoff = DateTimeOffset.UtcNow - started > TimeSpan.FromMinutes(1) ? TimeSpan.FromSeconds(1)
                : TimeSpan.FromSeconds(Math.Min(60, backoff.TotalSeconds * 2));
            try { await Task.Delay(backoff, cancellationToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private void Report(string text) => context.Post(_ => { if (!lifetime.IsCancellationRequested) Diagnostic?.Invoke(text); }, null);

    public void Dispose()
    {
        if (lifetime.IsCancellationRequested) return;
        lifetime.Cancel();
        try { helper?.Kill(); } catch (InvalidOperationException) { }
    }
}
