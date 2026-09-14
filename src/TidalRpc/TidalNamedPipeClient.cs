using DiscordRPC.IO;
using DiscordRPC.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TidalRpc;

// The pinned RPC library does not expose activity.name. Its public transport seam
// lets us add that field while retaining its framing, reconnect, and clear behavior.
public sealed class TidalNamedPipeClient : INamedPipeClient
{
    private readonly ManagedNamedPipeClient inner = new();
#if DEBUG
    private readonly HttpClient artworkHttp = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly CancellationTokenSource lifetime = new();
    private string? lastArtwork;
#endif
    public event Action<string>? ActivityResponse;
    public ILogger Logger { get => inner.Logger; set => inner.Logger = value; }
    public bool IsConnected => inner.IsConnected;
#pragma warning disable CS0618
    public int ConnectedPipe => inner.ConnectedPipe;
#pragma warning restore CS0618
    public bool Connect(int pipe) => inner.Connect(pipe);
    public bool ReadFrame(out PipeFrame frame)
    {
        bool read = inner.ReadFrame(out frame);
        if (read && frame.Opcode == Opcode.Frame)
        {
            var response = JObject.Parse(frame.Message);
            if ((string?)response["cmd"] == "SET_ACTIVITY")
            {
                var data = response["data"] as JObject;
                DebugConsole.Write("Discord ACK", $"nonce={response["nonce"]} event={response["evt"]} code={data?["code"]} title={data?["details"]} artist={data?["state"]} type={data?["type"]}");
                DebugConsole.Write("Artwork ACK", $"nonce={response["nonce"]} image={data?["assets"]?["large_image"] ?? "<none>"}");
                ActivityResponse?.Invoke(frame.Message);
            }
        }
        return read;
    }
    public bool WriteFrame(PipeFrame frame)
    {
        var outgoing = WithTidalName(frame);
        var written = inner.WriteFrame(outgoing);
        if (outgoing.Opcode == Opcode.Frame)
        {
            var payload = JObject.Parse(outgoing.Message);
            if ((string?)payload["cmd"] == "SET_ACTIVITY")
            {
                var activity = payload["args"]?["activity"] as JObject;
                DebugConsole.Write("Discord SEND", $"written={written} nonce={payload["nonce"]} clear={activity is null || activity.Type == JTokenType.Null} title={activity?["details"]} artist={activity?["state"]} type={activity?["type"]} artwork={!string.IsNullOrEmpty((string?)activity?["assets"]?["large_image"])}");
                var artwork = (string?)activity?["assets"]?["large_image"];
                var nonce = (string?)payload["nonce"];
                DebugConsole.Write("Artwork SEND", $"nonce={nonce} url={artwork ?? "<none>"}");
#if DEBUG
                if (artwork != lastArtwork)
                {
                    lastArtwork = artwork;
                    if (Uri.TryCreate(artwork, UriKind.Absolute, out var uri) && uri.Scheme == "https")
                        _ = CheckArtworkAsync(uri, nonce, lifetime.Token);
                }
#endif
            }
        }
        return written;
    }
    public void Close() => inner.Close();
    public void Dispose()
    {
#if DEBUG
        lifetime.Cancel();
#endif
        inner.Dispose();
#if DEBUG
        artworkHttp.Dispose(); lifetime.Dispose();
#endif
    }

#if DEBUG
    private async Task CheckArtworkAsync(Uri uri, string? nonce, CancellationToken cancellationToken)
    {
        try
        {
            // Headers-only GET: no credentials, no full image download, no delay to RPC.
            using var response = await artworkHttp.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            DebugConsole.Write("Artwork HTTP", $"nonce={nonce} status={(int)response.StatusCode} contentType={response.Content.Headers.ContentType?.ToString() ?? "<none>"} bytes={response.Content.Headers.ContentLength?.ToString() ?? "unknown"}");
            DebugConsole.Write("Artwork HTTP", $"nonce={nonce} finalUrl={response.RequestMessage?.RequestUri} (checked from this PC, not Discord)");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { DebugConsole.Write("Artwork HTTP", $"nonce={nonce} check failed: {ex.GetType().Name} (checked from this PC)"); }
    }
#endif

    public static PipeFrame WithTidalName(PipeFrame frame)
    {
        if (frame.Opcode != Opcode.Frame) return frame;
        var payload = JObject.Parse(frame.Message);
        if ((string?)payload["cmd"] == "SET_ACTIVITY" && payload["args"]?["activity"] is JObject activity)
        {
            activity["name"] = "TIDAL";
            // Keep all credited artists in the card's state. In Artist status mode,
            // route the member list through name so it only shows the first artist.
            if ((int?)activity["status_display_type"] == 1 && (string?)activity["state"] is { Length: > 0 } artists)
            {
                activity["name"] = artists.Split(DiscordPresencePublisher.ArtistSeparator, StringSplitOptions.None)[0];
                activity["status_display_type"] = 0;
            }
            frame.Message = payload.ToString(Formatting.None);
        }
        return frame;
    }
}
