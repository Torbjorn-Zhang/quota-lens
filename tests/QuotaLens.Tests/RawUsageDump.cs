using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using QuotaLens.Services;

namespace QuotaLens;

/// <summary>
/// Diagnostic helper: prints the structural shape of the live Claude usage response. String values
/// are replaced by "&lt;string len=N&gt;" except for a short allowlist of structural keys (kind,
/// resets_at, ...), <c>scope.model.display_name</c>, and model ids that start with "claude-", so
/// upstream format changes can be inspected without account identifiers ending up in logs or issues.
/// </summary>
internal static class RawUsageDump
{
    private static readonly HashSet<string> KeepStringKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "kind", "resets_at", "reset_at", "type", "unit", "window", "period", "scope_type"
    };

    internal static async Task RunAsync()
    {
        var credential = await CredentialReader.ReadClaudeAsync(CancellationToken.None);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var version = typeof(QuotaService).Assembly.GetName().Version?.ToString(3) ?? "dev";
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"QuotaLens/{version} Windows");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        request.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
        using var response = await http.SendAsync(request);
        Console.WriteLine($"RAW_USAGE HTTP {(int)response.StatusCode}");
        var body = await response.Content.ReadAsStringAsync();
        try
        {
            using var document = JsonDocument.Parse(body);
            Console.WriteLine(Describe(document.RootElement));
        }
        catch (JsonException)
        {
            Console.WriteLine($"RAW_USAGE non-JSON body (length {body.Length})");
        }
    }

    internal static string Describe(JsonElement root)
    {
        var builder = new StringBuilder();
        Write(root, builder, 0, key: null, parentKey: null);
        return builder.ToString();
    }

    private static void Write(
        JsonElement element,
        StringBuilder builder,
        int indent,
        string? key,
        string? parentKey)
    {
        var pad = new string(' ', indent * 2);
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                builder.AppendLine("{");
                foreach (var property in element.EnumerateObject())
                {
                    builder.Append(pad).Append("  \"").Append(property.Name).Append("\": ");
                    Write(property.Value, builder, indent + 1, property.Name, key);
                }
                builder.Append(pad).AppendLine("}");
                break;
            case JsonValueKind.Array:
                builder.AppendLine("[");
                foreach (var item in element.EnumerateArray())
                {
                    builder.Append(pad).Append("  ");
                    Write(item, builder, indent + 1, key, parentKey);
                }
                builder.Append(pad).AppendLine("]");
                break;
            case JsonValueKind.String:
                var value = element.GetString() ?? string.Empty;
                builder.AppendLine(ShouldKeep(key, parentKey, value)
                    ? $"\"{value}\""
                    : $"\"<string len={value.Length}>\"");
                break;
            default:
                builder.AppendLine(element.GetRawText());
                break;
        }
    }

    private static bool ShouldKeep(string? key, string? parentKey, string value)
    {
        if (key is null) return false;
        if (KeepStringKeys.Contains(key)) return true;

        var underModel = string.Equals(parentKey, "model", StringComparison.OrdinalIgnoreCase);
        if (underModel && key.Equals("display_name", StringComparison.OrdinalIgnoreCase)) return true;

        return key.Equals("id", StringComparison.OrdinalIgnoreCase)
               && value.StartsWith("claude-", StringComparison.OrdinalIgnoreCase);
    }
}
