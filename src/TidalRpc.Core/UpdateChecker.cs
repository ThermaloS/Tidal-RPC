using System.Net;
using System.Text.Json;

namespace TidalRpc;

public static class UpdateChecker
{
    public static Version? NewerVersion(string tag, string current)
    {
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)
            || !Version.TryParse(current, out var installed)) return null;
        return latest > installed ? latest : null;
    }

    public static async Task<Version?> CheckAsync(Uri repository, string current, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 1024 * 1024 };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TidalRpc/" + current);
        using var response = await client.GetAsync("https://api.github.com/repos" + repository.AbsolutePath.TrimEnd('/') + "/releases/latest", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var release = json.RootElement;
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return null;
        return NewerVersion(release.GetProperty("tag_name").GetString() ?? "", current);
    }
}
