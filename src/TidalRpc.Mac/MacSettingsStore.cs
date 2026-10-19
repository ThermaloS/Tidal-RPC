using System.Text.Json;
using System.Text.Json.Serialization;

namespace TidalRpc;

public static class MacSettingsStore
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "TidalRpc");
    public static string FilePath => Path.Combine(Folder, "settings.json");
    // Start at login is the LaunchAgent itself, so the Windows-only fields aren't stored.
    internal sealed record StoredSettings(string Country = "US", bool Enabled = true, bool ShowArtwork = true,
        bool ShowPlaybackTime = true, bool ShowTidalButton = true, StatusTextMode StatusText = StatusTextMode.Artist);

    public static DateTime Stamp => File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : default;
    public static AppSettings Load() => File.Exists(FilePath) ? Deserialize(File.ReadAllText(FilePath)) : new();
    public static AppSettings Deserialize(string json)
    {
        var s = JsonSerializer.Deserialize(json, MacSettingsJson.Default.StoredSettings) ?? throw new InvalidDataException();
        var settings = new AppSettings(s.Country, s.Enabled, false, s.ShowArtwork, s.ShowPlaybackTime, s.ShowTidalButton, s.StatusText);
        Validate(settings);
        return settings;
    }
    public static string Serialize(AppSettings s) => JsonSerializer.Serialize(
        new StoredSettings(s.Country, s.Enabled, s.ShowArtwork, s.ShowPlaybackTime, s.ShowTidalButton, s.StatusText), MacSettingsJson.Default.StoredSettings);
    public static void Save(AppSettings s)
    {
        Validate(s);
        Directory.CreateDirectory(Folder);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, Serialize(s));
        File.Move(temp, FilePath, true);
    }
    public static void Validate(AppSettings s)
    {
        if (!MetadataRules.IsCountryCode(s.Country)) throw new InvalidOperationException("Use a valid country code, such as US or GB.");
        if (!Enum.IsDefined(s.StatusText)) throw new InvalidOperationException("Choose a valid display preference.");
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(MacSettingsStore.StoredSettings))]
internal sealed partial class MacSettingsJson : JsonSerializerContext;
