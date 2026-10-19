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
