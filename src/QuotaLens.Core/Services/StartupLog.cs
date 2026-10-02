using System.IO;

namespace QuotaLens.Services;

/// <summary>
/// Append-only local diagnostics for the process lifecycle: start, exit, autostart registration and
/// unhandled errors. Lines carry a timestamp, the version, launch arguments and short messages only,
/// never tokens, responses or account identifiers. The file is trimmed to the last 200 lines.
/// </summary>
internal static class StartupLog
{
    private const int MaxLines = 200;
    private static readonly object Gate = new();

    internal static string LogPath { get; } = Path.Combine(AppPaths.DataDirectory, "startup.log");

    internal static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                var lines = File.Exists(LogPath)
                    ? File.ReadAllLines(LogPath).ToList()
                    : new List<string>();
                lines.Add($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} {message}");
                if (lines.Count > MaxLines)
                {
                    lines.RemoveRange(0, lines.Count - MaxLines);
                }
                File.WriteAllLines(LogPath, lines);
            }
        }
        catch
        {
            // Diagnostics must never affect the widget itself.
        }
    }
}
