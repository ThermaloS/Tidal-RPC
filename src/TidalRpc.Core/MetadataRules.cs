using System.Globalization;

namespace TidalRpc;

public static class MetadataRules
{
    private static readonly HashSet<string> Countries = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
        .Select(c => new RegionInfo(c.Name).TwoLetterISORegionName).Where(c => c.Length == 2).ToHashSet(StringComparer.Ordinal);
    public static bool IsCountryCode(string? country) => country is not null && Countries.Contains(country);
    public static bool IsHttps(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.UserInfo.Length == 0 && url.Length <= 256;
    public static bool IsArtworkUrl(string? url) => IsHttps(url)
        && Path.GetExtension(new Uri(url!).AbsolutePath).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp";
    public static bool IsTidalUrl(string? url) => IsHttps(url)
        && (new Uri(url!).Host.Equals("tidal.com", StringComparison.OrdinalIgnoreCase)
            || new Uri(url!).Host.EndsWith(".tidal.com", StringComparison.OrdinalIgnoreCase));
}

public sealed class MetadataCooldownException(DateTimeOffset notBefore) : InvalidOperationException("TIDAL is busy. Retrying shortly.")
{
    public DateTimeOffset NotBefore { get; } = notBefore;
}

public sealed record MetadataRequest(string Title, string Artist, string Album, string Country, double? DurationSeconds);
