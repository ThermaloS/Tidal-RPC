using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;

namespace TidalRpc;

// Start at login: a per-user launchd agent that runs this executable in the background.
public static partial class LaunchAgent
{
    public const string Label = "com.thermalos.tidalrpc";
    public static string PlistPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", Label + ".plist");
    public static bool Installed => File.Exists(PlistPath);

    // Background jobs get lower CPU and I/O priority from macOS; KeepAlive restarts it only after a crash.
    public static string CreatePlist(string executable) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
          <key>Label</key><string>{Label}</string>
          <key>ProgramArguments</key><array><string>{SecurityElement.Escape(executable)}</string><string>run</string></array>
          <key>RunAtLoad</key><true/>
          <key>KeepAlive</key><dict><key>SuccessfulExit</key><false/></dict>
          <key>ProcessType</key><string>Background</string>
          <key>LowPriorityIO</key><true/>
          <key>ThrottleInterval</key><integer>30</integer>
        </dict>
        </plist>

        """;

    public static void Install()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Couldn't find this app's location.");
        if (executable.Contains("/AppTranslocation/", StringComparison.Ordinal))
            throw new InvalidOperationException("Drag TIDAL RPC to Applications, then open it from there.");
        Directory.CreateDirectory(Path.GetDirectoryName(PlistPath)!);
        Launchctl("bootout", $"gui/{getuid()}/{Label}");
        File.WriteAllText(PlistPath, CreatePlist(executable));
        if (Launchctl("bootstrap", $"gui/{getuid()}", PlistPath) != 0) throw new InvalidOperationException("launchctl couldn't start TIDAL RPC.");
    }

    public static void Uninstall()
    {
        Launchctl("bootout", $"gui/{getuid()}/{Label}");
        File.Delete(PlistPath);
    }

    private static int Launchctl(params string[] args)
    {
        using var process = Process.Start(new ProcessStartInfo("/bin/launchctl", args) { RedirectStandardError = true, UseShellExecute = false })!;
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }

    [LibraryImport("libc")]
    private static partial uint getuid();
}
