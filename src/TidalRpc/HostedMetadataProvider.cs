using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TidalRpc;

public sealed class HostedMetadataProvider(HttpClient http, Uri baseUri, string country, TimeProvider? clock = null) : ITrackMetadataProvider
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly Dictionary<string, (DateTimeOffset Until, TrackMetadata Value)> cache = [];
    private DateTimeOffset retryAt;
    public async Task<TrackMetadata?> ResolveAsync(PlaybackSnapshot track, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (retryAt > time.GetUtcNow()) throw new MetadataCooldownException(retryAt);
        if (cache.TryGetValue(track.Key, out var cached) && cached.Until > time.GetUtcNow()) return cached.Value;
        // ResponseHeadersRead stops HttpClient's timeout when headers arrive.
        // Keep a deadline through the body read so a stalled response can retry.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(http.Timeout != Timeout.InfiniteTimeSpan && http.Timeout < TimeSpan.FromSeconds(30)
            ? http.Timeout : TimeSpan.FromSeconds(30));
        var requestToken = deadline.Token;
        static string Bound(string text) => text[..Math.Min(text.Length, 256)];
        var payload = new MetadataRequest(Bound(track.Title), Bound(track.Artist), Bound(track.Album), country,
            track.Duration is { } duration && duration > TimeSpan.Zero ? duration.TotalSeconds : null);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "v1/metadata/resolve")) { Content = JsonContent.Create(payload) };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestToken);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            retryAt = response.Headers.RetryAfter?.Date ?? time.GetUtcNow() + (response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(60));
            throw new MetadataCooldownException(retryAt);
        }
        if (response.StatusCode == HttpStatusCode.NoContent) return null;
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 65536) throw new InvalidDataException("Metadata response was too large.");
        using var stream = await response.Content.ReadAsStreamAsync(requestToken);
        using var bytes = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(buffer, requestToken)) > 0)
        {
            if (bytes.Length + read > 65536) throw new InvalidDataException("Metadata response was too large.");
            bytes.Write(buffer, 0, read);
        }
        var result = JsonSerializer.Deserialize<TrackMetadata>(bytes.ToArray(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (result is not null && MetadataRules.IsArtworkUrl(result.ArtworkUrl) && MetadataRules.IsTidalUrl(result.TrackUrl)
            && !string.IsNullOrWhiteSpace(result.Album))
        {
            if (cache.Count >= 256) cache.Clear();
            cache[track.Key] = (time.GetUtcNow().AddHours(1), result);
        }
        return result;
    }
}

internal sealed class UnavailableMetadataProvider : ITrackMetadataProvider
{
    public Task<TrackMetadata?> ResolveAsync(PlaybackSnapshot track, CancellationToken cancellationToken) => Task.FromResult<TrackMetadata?>(null);
}
