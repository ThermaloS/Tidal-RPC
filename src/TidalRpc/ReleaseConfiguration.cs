using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TidalRpc;

public sealed record ReleaseSettings(string RepositoryUrl = "", string MetadataServiceUrl = "");
public static class ReleaseConfiguration
{
    public static string Version => typeof(ReleaseConfiguration).Assembly.GetName().Version?.ToString(3) ?? "0.2.0";
    public static ReleaseSettings Settings { get; } = Load();
    private static ReleaseSettings Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TidalRpc.ReleaseSettings.json");
        return stream is null ? new() : JsonSerializer.Deserialize<ReleaseSettings>(stream) ?? new();
    }
    public static Uri? Repository => RepositoryUri(Settings.RepositoryUrl);
    public static Uri? MetadataService => ServiceUri(Settings.MetadataServiceUrl);
    public static Uri? RepositoryUri(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.Host == "github.com" && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && uri.Query.Length == 0 && uri.Fragment.Length == 0
        && Regex.IsMatch(uri.AbsolutePath, @"^/[A-Za-z0-9-]+/[A-Za-z0-9_.-]+/?$")
        ? new Uri(uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/") : null;
    public static Uri? ServiceUri(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
        ? new Uri(uri.AbsoluteUri.TrimEnd('/') + "/") : null;
}
