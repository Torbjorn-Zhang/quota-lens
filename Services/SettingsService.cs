using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace QuotaLens.Services;

public sealed class SettingsService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "QuotaLens";
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

    public void SetStartWithWindows(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
        {
            var executable = Environment.ProcessPath
                             ?? throw new InvalidOperationException("无法确定程序路径。");
            key.SetValue(RunValueName, $"\"{executable}\"");
        }
        else
        {
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
    }
}
