using System.Diagnostics;
using System.Security;
using QuotaLens.Services;

namespace QuotaLens.Mac;

/// <summary>macOS integration done through standard system tools and files; no private APIs.</summary>
internal static class MacPlatform
{
    private const string AgentLabel = "io.github.torbjorn-zhang.quotalens";
    private static Process? _caffeinate;

    private static string AgentPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library",
        "LaunchAgents",
        AgentLabel + ".plist");

    /// <summary>Path of the enclosing <c>QuotaLens.app</c>, or null when run outside a bundle.</summary>
    internal static string? BundlePath
    {
        get
        {
            var executable = Environment.ProcessPath;
            if (executable is null) return null;
            var marker = executable.IndexOf(".app/Contents/MacOS/", StringComparison.Ordinal);
            return marker < 0 ? null : executable[..(marker + ".app".Length)];
        }
    }

    internal static bool IsLaunchAtLoginRegistered => File.Exists(AgentPath);

    /// <summary>
    /// Registers or removes a per-user LaunchAgent that opens the app at login. Rewritten on every
    /// launch while enabled so the agent follows the app if it is moved.
    /// </summary>
    internal static void SetLaunchAtLogin(bool enabled)
    {
        if (!enabled)
        {
            if (File.Exists(AgentPath)) File.Delete(AgentPath);
            StartupLog.Write("autostart: launch agent removed");
            return;
        }

        var arguments = BundlePath is string bundle
            ? new[] { "/usr/bin/open", bundle }
            : new[] { Environment.ProcessPath ?? throw new InvalidOperationException("无法确定程序路径。") };
        var plist = new System.Text.StringBuilder()
            .AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>")
            .AppendLine("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">")
            .AppendLine("<plist version=\"1.0\">")
            .AppendLine("<dict>")
            .AppendLine("    <key>Label</key>")
            .AppendLine($"    <string>{AgentLabel}</string>")
            .AppendLine("    <key>ProgramArguments</key>")
            .AppendLine("    <array>");
        foreach (var argument in arguments)
        {
            plist.AppendLine($"        <string>{SecurityElement.Escape(argument)}</string>");
        }
        plist
            .AppendLine("    </array>")
            .AppendLine("    <key>RunAtLoad</key>")
            .AppendLine("    <true/>")
            .AppendLine("    <key>LimitLoadToSessionType</key>")
            .AppendLine("    <string>Aqua</string>")
            .AppendLine("    <key>ProcessType</key>")
            .AppendLine("    <string>Interactive</string>")
            .AppendLine("</dict>")
            .AppendLine("</plist>");

        Directory.CreateDirectory(Path.GetDirectoryName(AgentPath)!);
        File.WriteAllText(AgentPath, plist.ToString());
        StartupLog.Write($"autostart: launch agent -> {string.Join(" ", arguments)}");
    }

    /// <summary>Posts a notification through AppleScript; failures are ignored.</summary>
    internal static void Notify(string title, string message)
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            var script = $"display notification \"{EscapeAppleScript(message)}\" with title \"{EscapeAppleScript(title)}\"";
            var start = new ProcessStartInfo("/usr/bin/osascript") { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("-e");
            start.ArgumentList.Add(script);
            Process.Start(start)?.Dispose();
        }
        catch (Exception ex)
        {
            StartupLog.Write($"notify failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Keeps the system awake for as long as Quota Lens runs (<c>caffeinate -i -w pid</c>) and then
    /// turns the displays off; any input wakes them.
    /// </summary>
    internal static void TurnOffDisplaysKeepingAwake()
    {
        if (!OperatingSystem.IsMacOS()) return;
        if (_caffeinate is null || _caffeinate.HasExited)
        {
            var keepAwake = new ProcessStartInfo("/usr/bin/caffeinate") { UseShellExecute = false, CreateNoWindow = true };
            keepAwake.ArgumentList.Add("-i");
            keepAwake.ArgumentList.Add("-w");
            keepAwake.ArgumentList.Add(Environment.ProcessId.ToString());
            _caffeinate = Process.Start(keepAwake);
        }

        var displayOff = new ProcessStartInfo("/usr/bin/pmset") { UseShellExecute = false, CreateNoWindow = true };
        displayOff.ArgumentList.Add("displaysleepnow");
        Process.Start(displayOff)?.Dispose();
    }

    internal static void StopKeepingAwake()
    {
        try
        {
            if (_caffeinate is { HasExited: false }) _caffeinate.Kill();
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }

    private static string EscapeAppleScript(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
