using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace QuotaLens.Services;

internal static class CredentialReader
{
    internal sealed record CodexCredential(string AccessToken, string AccountId);
    internal sealed record ClaudeCredential(string AccessToken);

    public static async Task<CodexCredential> ReadCodexAsync(CancellationToken cancellationToken)
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_HOME");
        var directory = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex")
            : Environment.ExpandEnvironmentVariables(configured);
        var path = Path.Combine(directory, "auth.json");

        using var document = await ReadJsonAsync(path, "尚未找到 Codex 登录信息，请先在 Codex 中登录。", cancellationToken);
        var root = document.RootElement;

        var token = GetString(root, "tokens", "access_token")
                    ?? GetString(root, "access_token");
        var accountId = GetString(root, "tokens", "account_id")
                        ?? GetString(root, "account_id");

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(accountId))
        {
            throw new QuotaException("Codex 当前不是 ChatGPT 账号登录，无法读取订阅额度。");
        }

        return new CodexCredential(token, accountId);
    }

    public static async Task<ClaudeCredential> ReadClaudeAsync(CancellationToken cancellationToken)
    {
        var configured = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var directory = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude")
            : Environment.ExpandEnvironmentVariables(configured);
        var path = Path.Combine(directory, ".credentials.json");

        if (File.Exists(path))
        {
            using var document = await ReadJsonAsync(path, "Claude Code 登录信息不可用。", cancellationToken);
            var root = document.RootElement;
            var fileToken = GetString(root, "claudeAiOauth", "accessToken")
                            ?? GetString(root, "oauthAccount", "accessToken")
                            ?? GetString(root, "accessToken");

            if (!string.IsNullOrWhiteSpace(fileToken))
            {
                return new ClaudeCredential(fileToken);
            }
        }

        var desktopCache = await ReadClaudeDesktopCacheAsync(cancellationToken);
        var token = FindClaudeDesktopToken(desktopCache)
                    ?? FindStringByPropertyName(desktopCache, "accessToken")
                    ?? FindStringByPropertyName(desktopCache, "access_token")
                    ?? FindStringByPropertyName(desktopCache, "oauthAccessToken");

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new QuotaException("已找到 Claude 桌面版登录态，但其中没有可用的 Claude Code OAuth 凭据。");
        }

        return new ClaudeCredential(token);
    }

    internal static async Task<JsonElement> ReadClaudeDesktopCacheAsync(CancellationToken cancellationToken)
    {
        var dataDirectory = FindClaudeDesktopDataDirectory();
        if (dataDirectory is null)
        {
            throw new QuotaException("尚未找到 Claude Code 登录信息。请先在 Claude Code 或 Claude 桌面版中登录。");
        }

        var configPath = Path.Combine(dataDirectory, "config.json");
        var localStatePath = Path.Combine(dataDirectory, "Local State");
        using var config = await ReadJsonAsync(configPath, "Claude 桌面版配置不可用。", cancellationToken);
        using var localState = await ReadJsonAsync(localStatePath, "Claude 桌面版安全存储不可用。", cancellationToken);

        var encryptedCache = GetString(config.RootElement, "oauth:tokenCacheV2")
                             ?? GetString(config.RootElement, "oauth:tokenCache");
        var encryptedKey = GetString(localState.RootElement, "os_crypt", "encrypted_key");
        if (string.IsNullOrWhiteSpace(encryptedCache) || string.IsNullOrWhiteSpace(encryptedKey))
        {
            throw new QuotaException("Claude 桌面版存在，但当前没有可用的登录缓存。");
        }

        try
        {
            var keyBytes = Convert.FromBase64String(encryptedKey);
            var dpapiPrefix = Encoding.ASCII.GetBytes("DPAPI");
            if (!keyBytes.AsSpan().StartsWith(dpapiPrefix))
            {
                throw new CryptographicException("不支持的安全存储密钥格式。");
            }

            var masterKey = ProtectedData.Unprotect(
                keyBytes.AsSpan(dpapiPrefix.Length).ToArray(),
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
            var encryptedBytes = Convert.FromBase64String(encryptedCache);
            byte[] plaintext;
            try
            {
                plaintext = DecryptElectronSafeStorage(encryptedBytes, masterKey);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(masterKey);
            }

            try
            {
                using var parsed = JsonDocument.Parse(plaintext);
                return parsed.RootElement.Clone();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException)
        {
            throw new QuotaException($"无法解锁 Claude 桌面版登录态：{ex.Message}");
        }
    }

    private static string? FindClaudeDesktopDataDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("CLAUDE_DESKTOP_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var expanded = Environment.ExpandEnvironmentVariables(configured);
            if (File.Exists(Path.Combine(expanded, "config.json"))) return expanded;
        }

        var roamingCandidate = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Claude");
        if (File.Exists(Path.Combine(roamingCandidate, "config.json"))) return roamingCandidate;

        var packages = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Packages");
        if (!Directory.Exists(packages)) return null;

        foreach (var package in Directory.EnumerateDirectories(packages, "Claude_*"))
        {
            var candidate = Path.Combine(package, "LocalCache", "Roaming", "Claude");
            if (File.Exists(Path.Combine(candidate, "config.json"))) return candidate;
        }

        return null;
    }

    private static byte[] DecryptElectronSafeStorage(byte[] encrypted, byte[] masterKey)
    {
        if (encrypted.Length < 3 + 12 + 16)
        {
            throw new CryptographicException("登录缓存长度无效。");
        }

        var prefix = Encoding.ASCII.GetString(encrypted, 0, 3);
        if (prefix is not ("v10" or "v11"))
        {
            throw new CryptographicException("不支持的登录缓存版本。");
        }

        var nonce = encrypted.AsSpan(3, 12);
        var tag = encrypted.AsSpan(encrypted.Length - 16, 16);
        var cipherText = encrypted.AsSpan(15, encrypted.Length - 15 - 16);
        var plaintext = new byte[cipherText.Length];
        using var aes = new AesGcm(masterKey);
        aes.Decrypt(nonce, cipherText, tag, plaintext);
        return plaintext;
    }

    private static string? FindStringByPropertyName(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals(propertyName)
                    && property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }

                var nested = FindStringByPropertyName(property.Value, propertyName);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindStringByPropertyName(item, propertyName);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        }

        return null;
    }

    internal static string? FindClaudeDesktopToken(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;

        string? bestToken = null;
        long bestExpiry = long.MinValue;
        foreach (var entry in root.EnumerateObject())
        {
            if (!entry.Name.Contains("user:profile", StringComparison.OrdinalIgnoreCase)
                || entry.Value.ValueKind != JsonValueKind.Object
                || !entry.Value.TryGetProperty("token", out var tokenElement)
                || tokenElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var token = tokenElement.GetString();
            if (string.IsNullOrWhiteSpace(token)) continue;

            var expiry = 0L;
            if (entry.Value.TryGetProperty("expiresAt", out var expiryElement)
                && expiryElement.ValueKind == JsonValueKind.Number)
            {
                expiryElement.TryGetInt64(out expiry);
            }

            if (bestToken is null || expiry > bestExpiry)
            {
                bestToken = token;
                bestExpiry = expiry;
            }
        }

        return bestToken ?? FindStringByPropertyName(root, "token");
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        string path,
        string missingMessage,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new QuotaException(missingMessage);
        }

        Exception? lastError = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            }
            catch (IOException ex)
            {
                lastError = ex;
                await Task.Delay(120, cancellationToken);
            }
            catch (JsonException ex)
            {
                lastError = ex;
                await Task.Delay(120, cancellationToken);
            }
        }

        throw new QuotaException($"暂时无法读取登录信息：{lastError?.Message}");
    }

    private static string? GetString(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var part in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
            {
                return null;
            }
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }
}

internal sealed class QuotaException : Exception
{
    public QuotaException(string message) : base(message) { }
}
