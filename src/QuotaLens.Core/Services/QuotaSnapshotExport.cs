using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Unicode;

namespace QuotaLens.Services;

/// <summary>
/// Optional sanitised snapshot for other local tools (for example a script that pushes the numbers to
/// a Raspberry Pi status screen). The file has Agent Farm's quota.json shape: percentages, reset times
/// as epoch milliseconds, plan, status, short error text, extra info and the windows at 20% or less.
/// It never contains a token, account id, e-mail address or response body; error and extra text is
/// scrubbed of anything that looks like one, or like a file path. Written atomically (temp file, then
/// rename) after each refresh while the setting is on, and removed when it is turned off.
/// </summary>
internal sealed class QuotaSnapshotExport
{
    private const int MaxText = 120;

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        // Keep the Chinese window names readable; HTML-sensitive characters are still escaped.
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    // Applied to error and extra text before it leaves the app. Paths go first so their pieces
    // (user names, profile folders) are not left behind by the narrower patterns.
    private static readonly (Regex Pattern, string Replacement)[] Scrubbers =
    {
        (new Regex(@"(?:[A-Za-z]:)?[\\/]+(?:[^\s\\/""'“”]+[\\/]+)+[^\s""'“”，。）)]*", RegexOptions.Compiled), "<路径>"),
        (new Regex(@"(?i)\bbearer\s+\S+", RegexOptions.Compiled), "<凭据>"),
        (new Regex(@"[^\s@""'“”<>()（）]+@[^\s@""'“”<>()（）]+\.[A-Za-z]{2,}", RegexOptions.Compiled), "<邮箱>"),
        (new Regex(@"[A-Za-z0-9_\-.=+/]{24,}", RegexOptions.Compiled), "<已隐藏>")
    };

    private readonly string _path;
    private readonly Action<string> _log;
    private string? _lastProblem = string.Empty;   // "" = nothing logged yet

    internal QuotaSnapshotExport(string? path = null, Action<string>? log = null)
    {
        _path = path ?? DefaultPath;
        _log = log ?? StartupLog.Write;
    }

    /// <summary>
    /// <c>%APPDATA%\QuotaLens\quota.json</c> on Windows and
    /// <c>~/Library/Application Support/QuotaLens/quota.json</c> on macOS.
    /// </summary>
    internal static string DefaultPath { get; } = OperatingSystem.IsMacOS()
        ? Path.Combine(AppPaths.DataDirectory, "quota.json")
        : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "QuotaLens",
            "quota.json");

    /// <summary>Writes the snapshot. Never throws; a failure is logged once until it changes.</summary>
    internal void Write(QuotaSnapshot snapshot)
    {
        try
        {
            WriteAtomic(_path, Serialize(snapshot));
            Report(null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report($"{ex.GetType().Name}: {Scrub(ex.Message)}");
        }
    }

    /// <summary>Removes the exported file (and a leftover temp file) when the setting is turned off.</summary>
    internal void Delete()
    {
        try
        {
            File.Delete(_path);
            File.Delete(_path + ".tmp");
            _lastProblem = string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log($"snapshot export: could not remove the file ({ex.GetType().Name})");
        }
    }

    internal static byte[] Serialize(QuotaSnapshot snapshot)
    {
        var providers = new[] { snapshot.Claude, snapshot.Codex };
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, WriterOptions))
        {
            json.WriteStartObject();
            json.WriteNumber("fetchedAt", snapshot.FetchedAt.ToUnixTimeMilliseconds());

            json.WriteStartArray("providers");
            foreach (var provider in providers)
            {
                var status = StatusOf(provider);
                var hasData = status is "ok" or "stale";
                json.WriteStartObject();
                json.WriteString("provider", ProviderId(provider));
                json.WriteString("name", ProviderName(provider));
                json.WriteString("plan", Scrub(provider.Plan) ?? string.Empty);
                json.WriteString("status", status);
                json.WriteString("error", status switch
                {
                    "ok" => null,
                    "stale" => Scrub(provider.StaleNote),
                    _ => Scrub(provider.Error)
                });
                json.WriteString("extra", Scrub(ExtraWithoutStaleNote(provider)));
                json.WriteNumber("updatedAt", (provider.UpdatedAt ?? snapshot.FetchedAt).ToUnixTimeMilliseconds());

                json.WriteStartArray("windows");
                foreach (var window in hasData ? provider.Windows : Array.Empty<QuotaWindow>())
                {
                    json.WriteStartObject();
                    json.WriteString("kind", KindName(window));
                    json.WriteString("name", window.Name);
                    json.WriteString("label", QuotaWindowLegend.ShortLabel(window));
                    json.WriteNumber("usedPercent", Percent(window.UsedPercent));
                    json.WriteNumber("remainingPercent", Percent(window.RemainingPercent));
                    WriteMillis(json, "resetsAt", window.ResetsAt);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndArray();

            json.WriteStartArray("alerts");
            foreach (var provider in providers.Where(provider => StatusOf(provider) is "ok" or "stale"))
            {
                foreach (var window in provider.Windows.Where(window => window.RemainingPercent <= 20))
                {
                    json.WriteStartObject();
                    json.WriteString("provider", ProviderId(provider));
                    json.WriteString("name", window.Name);
                    json.WriteNumber("remainingPercent", Percent(window.RemainingPercent));
                    WriteMillis(json, "resetsAt", window.ResetsAt);
                    json.WriteEndObject();
                }
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }

        return buffer.ToArray();
    }

    /// <summary><c>ok</c> | <c>stale</c> (last good numbers kept) | <c>waiting</c> (keychain prompt) | <c>error</c>.</summary>
    internal static string StatusOf(ProviderQuota provider)
    {
        if (provider.IsWaiting) return "waiting";
        if (!provider.IsAvailable) return "error";
        return provider.StaleNote is null ? "ok" : "stale";
    }

    /// <summary>Removes anything that looks like a credential, e-mail address or path; at most 120 characters.</summary>
    internal static string? Scrub(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        foreach (var (pattern, replacement) in Scrubbers)
        {
            text = pattern.Replace(text, replacement);
        }
        text = text.Trim();
        return text.Length <= MaxText ? text : text[..(MaxText - 1)] + "…";
    }

    private static string ProviderId(ProviderQuota provider) =>
        provider.Provider.StartsWith("Claude", StringComparison.OrdinalIgnoreCase) ? "claude" : "codex";

    private static string ProviderName(ProviderQuota provider) =>
        provider.Provider.StartsWith("Claude", StringComparison.OrdinalIgnoreCase) ? "Claude" : "Codex";

    private static string KindName(QuotaWindow window) => QuotaWindowLegend.Classify(window) switch
    {
        QuotaWindowKind.Session => "session",
        QuotaWindowKind.Weekly => "weekly",
        QuotaWindowKind.Model => "model",
        _ => "other"
    };

    /// <summary>The UI appends the 429 note to the extra line; in the file it is the stale <c>error</c> instead.</summary>
    private static string? ExtraWithoutStaleNote(ProviderQuota provider)
    {
        var extra = provider.ExtraInfo;
        var note = provider.StaleNote;
        if (extra is null || note is null) return extra;
        if (extra == note) return null;
        return extra.EndsWith(" · " + note, StringComparison.Ordinal) ? extra[..^(note.Length + 3)] : extra;
    }

    private static int Percent(double value) =>
        (int)Math.Round(Math.Clamp(value, 0d, 100d), MidpointRounding.AwayFromZero);

    private static void WriteMillis(Utf8JsonWriter json, string name, DateTimeOffset? value)
    {
        if (value is DateTimeOffset time) json.WriteNumber(name, time.ToUnixTimeMilliseconds());
        else json.WriteNull(name);
    }

    /// <summary>
    /// Temp file next to the target, then a rename over it, so a reader never sees half a file. A reader
    /// holding the old file open on Windows can make the rename fail for a moment, so it is retried.
    /// </summary>
    private static void WriteAtomic(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, data);
        if (OperatingSystem.IsMacOS()) OwnerOnly(temp);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < 4 && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(100);
            }
        }
    }

    /// <summary>chmod 600: other local accounts have no business reading it (%APPDATA% is per-user already).</summary>
    [SupportedOSPlatform("macos")]
    private static void OwnerOnly(string path) => _ = chmod(path, 0b110_000_000);

    [DllImport("libc", SetLastError = true)]
    private static extern int chmod([MarshalAs(UnmanagedType.LPUTF8Str)] string path, ushort mode);

    private void Report(string? problem)
    {
        if (problem == _lastProblem) return;
        _log(problem is null ? $"snapshot export: writing {_path}" : $"snapshot export failed: {problem}");
        _lastProblem = problem;
    }
}
