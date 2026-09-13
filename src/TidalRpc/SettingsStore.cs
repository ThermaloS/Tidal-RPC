using System.Text.Json;
using Microsoft.Win32;

namespace TidalRpc;

public static class AppDefaults
{
    public const string DiscordApplicationId = "1548771525732335746";
}
public enum StatusTextMode { Artist, Song, AppName }
public enum AppTheme { Dark, Light, System }
public sealed record AppSettings(string Country = "US", bool Enabled = true, bool StartWithWindows = false,
    bool ShowArtwork = true, bool ShowPlaybackTime = true, bool ShowTidalButton = true,
    StatusTextMode StatusText = StatusTextMode.Artist, AppTheme Theme = AppTheme.Dark, bool OpenSettingsOnLaunch = false)
{
    public string DiscordApplicationId => AppDefaults.DiscordApplicationId;
}
public static class SettingsStore
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TidalRpc");
    private static string FilePath => Path.Combine(Folder, "settings.json");
    public static bool Exists => File.Exists(FilePath);
    private sealed record StoredSettings(string Country, bool Enabled, bool StartWithWindows,
        bool ShowArtwork = true, bool ShowPlaybackTime = true, bool ShowTidalButton = true,
        StatusTextMode StatusText = StatusTextMode.Artist, AppTheme Theme = AppTheme.Dark, bool OpenSettingsOnLaunch = false);
    public static AppSettings Load()
    {
        if (!File.Exists(FilePath)) return new();
        return Deserialize(File.ReadAllText(FilePath));
    }
    public static AppSettings Deserialize(string json)
    {
        var s = JsonSerializer.Deserialize<StoredSettings>(json) ?? throw new InvalidDataException();
        var settings = new AppSettings(s.Country, s.Enabled, s.StartWithWindows, s.ShowArtwork, s.ShowPlaybackTime,
            s.ShowTidalButton, s.StatusText, s.Theme, s.OpenSettingsOnLaunch);
        Validate(settings);
        return settings;
    }
    public static void Save(AppSettings s)
    {
        Validate(s);
        Directory.CreateDirectory(Folder);
        var stored = new StoredSettings(s.Country, s.Enabled, s.StartWithWindows, s.ShowArtwork, s.ShowPlaybackTime,
            s.ShowTidalButton, s.StatusText, s.Theme, s.OpenSettingsOnLaunch);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, true);
    }
    public static void Validate(AppSettings s)
    {
        if (!MetadataRules.IsCountryCode(s.Country)) throw new InvalidOperationException("Use a valid country code, such as US or GB.");
        if (!Enum.IsDefined(s.StatusText) || !Enum.IsDefined(s.Theme)) throw new InvalidOperationException("Choose a valid display preference.");
    }
    public static void SaveWithStartup(AppSettings next, AppSettings previous, Action<AppSettings>? save = null,
        Func<object?>? readStartup = null, Action<bool>? setStartup = null, Action<object?>? restoreStartup = null)
    {
        Validate(next);
        if (next.StartWithWindows == previous.StartWithWindows) { (save ?? Save)(next); return; }
        var original = (readStartup ?? ReadStartup)();
        (setStartup ?? SetStartup)(next.StartWithWindows);
        try { (save ?? Save)(next); }
        catch
        {
            try { (restoreStartup ?? RestoreStartup)(original); }
            catch (Exception ex) { DebugConsole.Write("Settings", $"Startup rollback failed: {ex.GetType().Name}"); throw new InvalidOperationException("Couldn't save startup. Check Windows startup settings."); }
            throw;
        }
    }
    private sealed record StartupValue(object Value, RegistryValueKind Kind);
    private static object? ReadStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue("TidalRpc", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is { } value
            ? new StartupValue(value, key.GetValueKind("TidalRpc")) : null;
    }
    private static void RestoreStartup(object? original)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (original is StartupValue value) key.SetValue("TidalRpc", value.Value, value.Kind);
        else key.DeleteValue("TidalRpc", false);
    }
    public static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("TidalRpc", $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue("TidalRpc", false);
    }
}
