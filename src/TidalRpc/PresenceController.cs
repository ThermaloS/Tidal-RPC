namespace TidalRpc;

// Called on the UI synchronization context; provider completions return to that context.
public sealed class PresenceController(IPresencePublisher publisher, ITrackMetadataProvider metadata,
    TimeProvider? clock = null) : IDisposable
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private PlaybackSnapshot? current;
    private TrackMetadata? enrichment;
    private CancellationTokenSource? lookup;
    private DateTimeOffset? pausedAt;
    private long generation;
    private bool enabled = true;
    private bool disposed;
    private DateTimeOffset nextLookup;
    private int lookupAttempts;
    private DateTimeOffset snapshotAt;
    private bool IsComplete => current is { } track && enrichment is { } data
        && !string.IsNullOrWhiteSpace(track.Title) && !string.IsNullOrWhiteSpace(track.Artist)
        && !string.IsNullOrWhiteSpace(string.IsNullOrWhiteSpace(track.Album) ? data.Album : track.Album)
        && MetadataRules.IsArtworkUrl(data.ArtworkUrl) && MetadataRules.IsTidalUrl(data.TrackUrl ?? "");
    private static readonly int[] RetryDelays = [5, 10, 30, 60, 120];
    private static readonly TimeSpan PauseGrace = TimeSpan.FromSeconds(5);
    public event Action<string>? Status;
    public void RefreshPresence() { if (!disposed) PublishCurrent(); }

    public void SetEnabled(bool value)
    {
        enabled = value;
        if (!value) { CancelLookup(); publisher.Clear(); }
        else if (current is { } track) { current = null; Update(track); }
    }

    public void Update(PlaybackSnapshot? track)
    {
        if (disposed) return;
        bool changed = current?.Key != track?.Key;
        bool restarting = current?.Status == PlaybackStatus.Stopped && track?.Status == PlaybackStatus.Playing;
        bool wasPaused = current?.Status == PlaybackStatus.Paused;
        current = track;
        snapshotAt = time.GetUtcNow();
        if (changed || restarting) { CancelLookup(); enrichment = null; pausedAt = null; nextLookup = default; lookupAttempts = 0; }
        if (!enabled || track is null || track.Status == PlaybackStatus.Stopped || string.IsNullOrWhiteSpace(track.Title))
        {
            CancelLookup(); publisher.Clear(); pausedAt = null;
            Status?.Invoke(enabled ? "Waiting for TIDAL" : "Sharing is off");
            return;
        }
        if (track.Status == PlaybackStatus.Paused)
        {
            if (!wasPaused || pausedAt is null) pausedAt = time.GetUtcNow();
        }
        else pausedAt = null;
        PublishCurrent();
        TryEnrich();
    }

    public void Tick()
    {
        if (disposed) return;
        if (pausedAt is { } since && time.GetUtcNow() - since >= PauseGrace) publisher.Clear();
        TryEnrich();
    }

    private void TryEnrich()
    {
        if (disposed || !enabled || current is not { Status: PlaybackStatus.Playing } track || string.IsNullOrWhiteSpace(track.Title)) return;
        if (!IsComplete && lookup is null && lookupAttempts <= RetryDelays.Length && time.GetUtcNow() >= nextLookup)
            _ = EnrichAsync(track);
    }

    private void PublishCurrent()
    {
        if (!enabled || current is null || current.Status == PlaybackStatus.Stopped) return;
        if (current.Status == PlaybackStatus.Paused)
        {
            // Pauses are local state only. Do not queue a paused card ahead of the next track.
            if (pausedAt is { } since && time.GetUtcNow() - since >= PauseGrace) publisher.Clear();
            Status?.Invoke("Paused");
            return;
        }
        if (!IsComplete)
        {
            var exhausted = lookup is null && lookupAttempts > RetryDelays.Length;
            if (exhausted) publisher.Clear();
            Status?.Invoke(exhausted ? "This track is unavailable." : "Loading track…");
            return;
        }
        var outgoing = current;
        if (current.Status == PlaybackStatus.Playing && current.Position is { } position && current.Duration is { } duration && duration > TimeSpan.Zero && position >= TimeSpan.Zero && position <= duration)
            outgoing = current with { Position = TimeSpan.FromSeconds(Math.Clamp((position + (time.GetUtcNow() - snapshotAt)).TotalSeconds, 0, duration.TotalSeconds)) };
        publisher.Publish(outgoing, enrichment);
        Status?.Invoke("Playing");
    }

    private async Task EnrichAsync(PlaybackSnapshot track)
    {
        var revision = generation;
        lookupAttempts++;
        DebugConsole.Write("Artwork", $"Lookup {lookupAttempts}/6: {track.Title} — {track.Artist}");
        DateTimeOffset? providerNotBefore = null;
        var request = new CancellationTokenSource();
        lookup = request;
        try
        {
            var result = await metadata.ResolveAsync(track, request.Token);
            if (disposed || revision != generation || request.IsCancellationRequested) return;
            if (result is not null) enrichment = result;
            DebugConsole.Write("Artwork", $"{track.Title}: match={result is not null}, artwork={MetadataRules.IsArtworkUrl(result?.ArtworkUrl)}");
            PublishCurrent();
        }
        catch (OperationCanceledException) { DebugConsole.Write("Artwork", $"Lookup cancelled: {track.Title}"); }
        catch (MetadataCooldownException ex) { providerNotBefore = ex.NotBefore; DebugConsole.Write("Artwork", $"Rate limited until {ex.NotBefore:HH:mm:ss}"); }
        catch (Exception ex) { DebugConsole.Write("Artwork", $"Lookup failed: {ex.GetType().Name}"); if (!disposed && revision == generation) Status?.Invoke("Couldn't load this track. Retrying…"); }
        finally
        {
            if (!disposed && revision == generation && !request.IsCancellationRequested)
            {
                nextLookup = lookupAttempts <= RetryDelays.Length
                    ? time.GetUtcNow().AddSeconds(RetryDelays[lookupAttempts - 1]) : DateTimeOffset.MaxValue;
                if (providerNotBefore is { } notBefore && notBefore > nextLookup) nextLookup = notBefore;
                if (!IsComplete && lookupAttempts > RetryDelays.Length && current?.Status == PlaybackStatus.Playing)
                {
                    publisher.Clear();
                    Status?.Invoke("This track is unavailable.");
                    DebugConsole.Write("Artwork", "Retries exhausted; presence cleared without publishing a partial track.");
                }
            }
            if (ReferenceEquals(lookup, request)) lookup = null;
            request.Dispose();
        }
    }

    private void CancelLookup() { generation++; lookup?.Cancel(); lookup = null; }
    public void Dispose() { disposed = true; CancelLookup(); publisher.Clear(); }
}
