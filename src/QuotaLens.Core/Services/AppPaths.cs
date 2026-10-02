using System.IO;

namespace QuotaLens.Services;

/// <summary>Per-user data directory for settings and diagnostics on each platform.</summary>
internal static class AppPaths
{
    /// <summary>
    /// <c>%LOCALAPPDATA%\QuotaLens</c> on Windows and <c>~/Library/Application Support/QuotaLens</c>
    /// on macOS (.NET 6 maps LocalApplicationData to <c>~/.local/share</c> there, which is not where
    /// macOS apps keep their data).
    /// </summary>
    internal static string DataDirectory { get; } = OperatingSystem.IsMacOS()
        ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "Application Support",
            "QuotaLens")
        : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuotaLens");

    /// <summary>Platform label used in the HTTP user agent.</summary>
    internal static string PlatformName => OperatingSystem.IsMacOS() ? "macOS" : "Windows";

    internal static string Version =>
        typeof(AppPaths).Assembly.GetName().Version?.ToString(3) ?? "dev";
}
