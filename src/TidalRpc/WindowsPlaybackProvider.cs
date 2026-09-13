using Windows.Media.Control;

namespace TidalRpc;

public sealed class WindowsPlaybackProvider : IPlaybackProvider
{
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private readonly List<GlobalSystemMediaTransportControlsSession> sessions = [];
    private readonly SynchronizationContext context = SynchronizationContext.Current ?? new SynchronizationContext();
    private long revision;
    private bool disposed;
    private bool reading;
    private int dirty = 1;
    private DateTimeOffset nextPoll;
    public event Action<PlaybackSnapshot?>? Changed;
    public event Action<string>? Diagnostic;
    public static bool IsTidal(string id) => id.Equals("TIDAL.exe", StringComparison.OrdinalIgnoreCase)
        || id.Equals("com.squirrel.TIDAL.TIDAL", StringComparison.OrdinalIgnoreCase)
        || id.Equals("TIDAL", StringComparison.OrdinalIgnoreCase)
        || id.StartsWith("TIDALMusicAS.TIDAL_", StringComparison.OrdinalIgnoreCase)
        || id.StartsWith("TIDALMusicAS.TIDAL!", StringComparison.OrdinalIgnoreCase);

    public async Task StartAsync()
    {
        var requested = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        if (disposed) return;
        manager = requested;
        manager.SessionsChanged += SessionsChanged;
        Rebind();
    }
    private void SessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
        => context.Post(_ => Rebind(), null);
    private void Rebind()
    {
        if (disposed || manager is null) return;
        Unbind();
        revision++;
        foreach (var session in manager.GetSessions().Where(s => IsTidal(s.SourceAppUserModelId)))
        {
            sessions.Add(session);
            session.MediaPropertiesChanged += MediaChanged;
            session.PlaybackInfoChanged += PlaybackChanged;
            session.TimelinePropertiesChanged += TimelineChanged;
        }
        _ = RefreshAsync();
    }
    private void MediaChanged(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => Refresh();
    private void PlaybackChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => Refresh();
    private void TimelineChanged(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) => Refresh();
    // Media players can emit timeline events very frequently. Coalesce them into one
    // read per UI tick rather than allocating async reads for every notification.
    public void Refresh() => Interlocked.Exchange(ref dirty, 1);
    public void Poll()
    {
        if (disposed) return;
        if (Interlocked.Exchange(ref dirty, 0) == 1 || DateTimeOffset.UtcNow >= nextPoll)
        {
            nextPoll = DateTimeOffset.UtcNow.AddSeconds(5);
            _ = RefreshAsync();
        }
    }
    private async Task RefreshAsync()
    {
        if (reading) { Refresh(); return; }
        reading = true;
        var version = ++revision;
        try
        {
            var session = sessions.OrderByDescending(s => s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing).FirstOrDefault();
            if (session is null) { Changed?.Invoke(null); return; }
            var properties = await session.TryGetMediaPropertiesAsync();
            if (disposed || version != revision) return;
            var info = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            var status = info.PlaybackStatus switch
            {
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => PlaybackStatus.Playing,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => PlaybackStatus.Paused,
                _ => PlaybackStatus.Stopped
            };
            TimeSpan? duration = timeline.EndTime > timeline.StartTime ? timeline.EndTime - timeline.StartTime : null;
            TimeSpan? position = duration is not null ? timeline.Position - timeline.StartTime : null;
            if (position is { } original && duration is { } length && status == PlaybackStatus.Playing)
            {
                var elapsed = DateTimeOffset.UtcNow - timeline.LastUpdatedTime;
                if (elapsed >= TimeSpan.Zero && elapsed < TimeSpan.FromDays(1))
                    position = TimeSpan.FromSeconds(Math.Min(length.TotalSeconds, original.TotalSeconds + elapsed.TotalSeconds * (info.PlaybackRate ?? 1)));
            }
            if (position is { } p && (p < TimeSpan.Zero || p > duration)) { duration = null; position = null; }
            Changed?.Invoke(new(session.SourceAppUserModelId, properties.Title, properties.Artist, properties.AlbumTitle, status, position, duration));
        }
        catch (Exception) { if (!disposed && version == revision) { Changed?.Invoke(null); Diagnostic?.Invoke("Windows media session unavailable; retrying"); } }
        finally { reading = false; }
    }
    private void Unbind()
    {
        foreach (var session in sessions)
        {
            session.MediaPropertiesChanged -= MediaChanged;
            session.PlaybackInfoChanged -= PlaybackChanged;
            session.TimelinePropertiesChanged -= TimelineChanged;
        }
        sessions.Clear();
    }
    public void Dispose() { disposed = true; revision++; if (manager is not null) manager.SessionsChanged -= SessionsChanged; Unbind(); }
}
