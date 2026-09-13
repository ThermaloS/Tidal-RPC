using DiscordRPC;

namespace TidalRpc;

public sealed class DiscordPresencePublisher : IPresencePublisher
{
    private readonly DiscordRpcClient? client;
    private bool hasPresence;
    private AppSettings settings = new();
    public void SetSettings(AppSettings value) => settings = value;
    // Null means the connection needs no attention in the settings window.
    public event Action<string?>? Status;
    public DiscordPresencePublisher(string applicationId)
    {
        if (!ulong.TryParse(applicationId, out var id) || id == 0) return;
        client = new DiscordRpcClient(applicationId, -1, null, true, new TidalNamedPipeClient());
        client.OnReady += (_, _) => Status?.Invoke(null);
        client.OnPresenceUpdate += (_, _) => Status?.Invoke(null);
        client.OnClose += (_, _) => Status?.Invoke("Reconnecting to Discord…");
        client.OnConnectionFailed += (_, _) => Status?.Invoke("Open Discord to share your music.");
        client.OnError += (_, _) => Status?.Invoke("Couldn't update Discord. Try restarting Discord.");
        string? previousStatus = "Connecting to Discord…";
        Status += text =>
        {
            if (Interlocked.Exchange(ref previousStatus, text) != text)
                DebugConsole.Write("Discord", text ?? "Discord connected");
        };
    }
    public void Start() { if (client is null) Status?.Invoke("Discord is unavailable in this mode."); else client.Initialize(); }
    public static RichPresence CreatePresence(PlaybackSnapshot track, TrackMetadata? metadata, DateTime? now = null, AppSettings? settings = null)
    {
        settings ??= new();
        var presence = new RichPresence
        {
            Type = ActivityType.Listening,
            StatusDisplay = settings.StatusText switch { StatusTextMode.Song => StatusDisplayType.Details, StatusTextMode.AppName => StatusDisplayType.Name, _ => StatusDisplayType.State },
            Details = Clip(track.Title),
            State = Clip(string.IsNullOrWhiteSpace(track.Artist) ? "TIDAL" : track.Artist)
        };
        var album = !string.IsNullOrWhiteSpace(track.Album) ? track.Album : metadata?.Album;
        var artwork = settings.ShowArtwork && MetadataRules.IsArtworkUrl(metadata?.ArtworkUrl) ? metadata!.ArtworkUrl : null;
        if (artwork is not null || !string.IsNullOrWhiteSpace(album))
            presence.Assets = new Assets { LargeImageKey = artwork, LargeImageText = string.IsNullOrWhiteSpace(album) ? null : Clip(album) };
        if (settings.ShowTidalButton && metadata?.TrackUrl is { } url && MetadataRules.IsTidalUrl(url))
            presence.Buttons = [new DiscordRPC.Button { Label = "Play on TIDAL", Url = url }];
        if (settings.ShowPlaybackTime && track.Status == PlaybackStatus.Playing && track.Position is { } position && track.Duration is { } duration && duration > TimeSpan.Zero && position >= TimeSpan.Zero && position <= duration)
        {
            var start = (now ?? DateTime.UtcNow) - position;
            presence.Timestamps = new Timestamps(start, start + duration);
        }
        return presence;
    }
    private static string Clip(string text)
    {
        // Discord limits text by UTF-8 bytes; never split a surrogate pair.
        var result = new System.Text.StringBuilder();
        int bytes = 0;
        foreach (var rune in text.EnumerateRunes()) { if (bytes + rune.Utf8SequenceLength > 128) break; result.Append(rune); bytes += rune.Utf8SequenceLength; }
        return result.Length == 0 ? "TIDAL" : result.ToString();
    }
    public void Publish(PlaybackSnapshot track, TrackMetadata? metadata)
    {
        if (track.Status != PlaybackStatus.Playing) return;
        try { client?.SetPresence(CreatePresence(track, metadata, settings: settings)); hasPresence = client is not null; }
        catch (Exception ex) { DebugConsole.Write("Discord", $"Publish failed: {ex.GetType().Name}"); Status?.Invoke("Couldn't update Discord. Try restarting Discord."); }
    }
    public void Clear() { if (hasPresence && client?.IsInitialized == true) client.ClearPresence(); hasPresence = false; }
    public void Dispose() { Clear(); client?.Dispose(); }
}
