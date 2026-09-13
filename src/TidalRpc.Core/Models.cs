namespace TidalRpc;

public enum PlaybackStatus { Stopped, Paused, Playing }
public sealed record PlaybackSnapshot(string Source, string Title, string Artist, string Album,
    PlaybackStatus Status, TimeSpan? Position = null, TimeSpan? Duration = null)
{
    public string Key => $"{Source}\n{Title}\n{Artist}\n{Album}";
}
public sealed record TrackMetadata(string Album, string? ArtworkUrl, string? TrackUrl);
public interface IPlaybackProvider : IDisposable
{
    event Action<PlaybackSnapshot?>? Changed;
    Task StartAsync();
}
public interface ITrackMetadataProvider
{
    Task<TrackMetadata?> ResolveAsync(PlaybackSnapshot track, CancellationToken cancellationToken);
}
public interface IPresencePublisher : IDisposable
{
    void Publish(PlaybackSnapshot track, TrackMetadata? metadata);
    void Clear();
}
