using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace QuotaLens.Services;

public sealed class SettingsService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "QuotaLens";
    private const string LogonTaskName = "QuotaLens";
    private readonly string _settingsPath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public SettingsService()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuotaLens");
        Directory.CreateDirectory(directory);
        _settingsPath = Path.Combine(directory, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath))
                       ?? new AppSettings();
            }
        }
        catch
        {
            // A damaged settings file should never prevent the monitor from starting.
        }

        return new AppSettings();
    }

    public void Save(AppSettings settings) =>
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, _jsonOptions));

    /// <summary>
    /// Registers or removes automatic start at sign-in. A per-user Task Scheduler logon task is used
    /// because Explorer's Run-key processing was observed to skip the entry silently on Windows 11
    /// (see CHANGELOG 0.4.8). The Run key remains a fallback when Task Scheduler is unavailable, and
    /// any legacy Run value is removed so the widget never starts twice.
    /// </summary>
    public void SetStartWithWindows(bool enabled)
    {
        if (!enabled)
        {
            try
            {
                LogonTask.Delete(LogonTaskName);
            }
            catch (Exception ex)
            {
                StartupLog.Write($"autostart: logon task delete failed ({ex.GetType().Name}: {ex.Message})");
            }
            SetRunValue(null);
            StartupLog.Write("autostart: disabled");
            return;
        }

        var executable = Environment.ProcessPath
                         ?? throw new InvalidOperationException("无法确定程序路径。");
        try
        {
            LogonTask.Register(LogonTaskName, executable);
            SetRunValue(null);
            StartupLog.Write(
                $"autostart: logon task registered -> {executable}; readback {LogonTask.Describe(LogonTaskName)}");
        }
        catch (Exception ex)
        {
            StartupLog.Write($"autostart: logon task failed ({ex.GetType().Name}: {ex.Message}); falling back to Run key");
            SetRunValue($"\"{executable}\"");
        }
    }

    private static void SetRunValue(string? value)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (value is null)
        {
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
        else
        {
            key.SetValue(RunValueName, value);
        }
    }
}
