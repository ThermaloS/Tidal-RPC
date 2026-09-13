namespace TidalRpc;

internal static class AppBrand
{
    private static readonly Lazy<Icon> icon = new(() =>
    {
        using var stream = typeof(AppBrand).Assembly.GetManifestResourceStream("TidalRpc.App.ico");
        return stream is null ? (Icon)SystemIcons.Application.Clone() : new Icon(stream);
    });
    public static Icon Icon => icon.Value;
}
