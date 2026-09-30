using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace QuotaLens.Services;

internal static class CredentialReader
{
    internal sealed record CodexCredential(string AccessToken, string AccountId);
    internal sealed record ClaudeCredential(
        string AccessToken,
        string? SubscriptionType = null,
        string? RateLimitTier = null);

    /// <summary>Keychain item Claude Code (the CLI) uses on macOS instead of <c>.credentials.json</c>.</summary>
    private const string ClaudeCodeKeychainService = "Claude Code-credentials";

    /// <summary>Keychain item holding the Electron safe-storage password of Claude Desktop on macOS.</summary>
    private const string ClaudeDesktopKeychainService = "Claude Safe Storage";
    private const string ClaudeDesktopKeychainAccount = "Claude Key";

    private static readonly TimeSpan KeychainDenialBackoff = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan KeychainPromptWait = TimeSpan.FromSeconds(20);
    private static readonly object KeychainGate = new();
    private static readonly Dictionary<string, DateTimeOffset> KeychainRetryAfter = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Task<byte[]?>> PendingKeychainReads = new(StringComparer.Ordinal);
    private static byte[]? _macDesktopKey;

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

    /// <summary>
    /// Lets the next read ask the macOS Keychain again after the user declined access. Called for a
    /// manual refresh; automatic refreshes keep backing off so a denial does not re-prompt every
    /// few minutes.
    /// </summary>
    internal static void AllowKeychainRetry()
    {
        lock (KeychainGate) KeychainRetryAfter.Clear();
    }

    public static async Task<ClaudeCredential> ReadClaudeAsync(CancellationToken cancellationToken)
    {
        var accountMetadata = await ReadClaudeAccountMetadataAsync(cancellationToken);
        var configured = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var directory = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude")
            : Environment.ExpandEnvironmentVariables(configured);
        var path = Path.Combine(directory, ".credentials.json");

        if (File.Exists(path))
        {
            using var document = await ReadJsonAsync(path, "Claude Code 登录信息不可用。", cancellationToken);
            var fromFile = FindClaudeCodeCredential(document.RootElement, accountMetadata);
            if (fromFile is not null) return fromFile;
        }

        if (OperatingSystem.IsMacOS())
        {
            var fromKeychain = await ReadClaudeCodeKeychainAsync(accountMetadata, cancellationToken);
            if (fromKeychain is not null) return fromKeychain;
        }

        var desktopCache = await ReadClaudeDesktopCacheAsync(cancellationToken);
        var desktopCredential = FindClaudeDesktopCredential(desktopCache);
        var token = desktopCredential?.AccessToken
                    ?? FindStringByPropertyName(desktopCache, "accessToken")
                    ?? FindStringByPropertyName(desktopCache, "access_token")
                    ?? FindStringByPropertyName(desktopCache, "oauthAccessToken");

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new QuotaException("已找到 Claude 桌面版登录态，但其中没有可用的 Claude Code OAuth 凭据。");
        }

        return new ClaudeCredential(
            token,
            accountMetadata.SubscriptionType ?? desktopCredential?.SubscriptionType,
            accountMetadata.RateLimitTier ?? desktopCredential?.RateLimitTier);
    }

    /// <summary>Reads the CLI's <c>{"claudeAiOauth": {...}}</c> document from a file or the Keychain.</summary>
    private static ClaudeCredential? FindClaudeCodeCredential(JsonElement root, ClaudeAccountMetadata accountMetadata)
    {
        var token = GetString(root, "claudeAiOauth", "accessToken")
                    ?? GetString(root, "oauthAccount", "accessToken")
                    ?? GetString(root, "accessToken");
        if (string.IsNullOrWhiteSpace(token)) return null;

        var subscriptionType = accountMetadata.SubscriptionType
                               ?? GetString(root, "claudeAiOauth", "subscriptionType")
                               ?? GetString(root, "oauthAccount", "subscriptionType");
        var rateLimitTier = accountMetadata.RateLimitTier
                            ?? GetString(root, "claudeAiOauth", "rateLimitTier")
                            ?? GetString(root, "oauthAccount", "rateLimitTier");
        return new ClaudeCredential(token, subscriptionType, rateLimitTier);
    }

    [SupportedOSPlatform("macos")]
    private static async Task<ClaudeCredential?> ReadClaudeCodeKeychainAsync(
        ClaudeAccountMetadata accountMetadata,
        CancellationToken cancellationToken)
    {
        var secret = await ReadKeychainSecretAsync(
            ClaudeCodeKeychainService,
            account: null,
            missingIsError: false,
            cancellationToken);
        if (secret is null) return null;
        try
        {
            using var document = JsonDocument.Parse(secret);
            return FindClaudeCodeCredential(document.RootElement, accountMetadata);
        }
        catch (JsonException)
        {
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    private static async Task<ClaudeAccountMetadata> ReadClaudeAccountMetadataAsync(
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude.json");
        if (!File.Exists(path)) return new ClaudeAccountMetadata(null, null);

        try
        {
            using var document = await ReadJsonAsync(path, string.Empty, cancellationToken);
            var root = document.RootElement;
            return new ClaudeAccountMetadata(
                GetString(root, "oauthAccount", "subscriptionType"),
                GetString(root, "oauthAccount", "organizationRateLimitTier")
                ?? GetString(root, "oauthAccount", "userRateLimitTier"));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new ClaudeAccountMetadata(null, null);
        }
    }

    internal static async Task<JsonElement> ReadClaudeDesktopCacheAsync(CancellationToken cancellationToken)
    {
        var dataDirectory = FindClaudeDesktopDataDirectory();
        if (dataDirectory is null)
        {
            throw new QuotaException("尚未找到 Claude Code 登录信息。请先在 Claude Code 或 Claude 桌面版中登录。");
        }

        var configPath = Path.Combine(dataDirectory, "config.json");
        using var config = await ReadJsonAsync(configPath, "Claude 桌面版配置不可用。", cancellationToken);
        var encryptedCache = GetString(config.RootElement, "oauth:tokenCacheV2")
                             ?? GetString(config.RootElement, "oauth:tokenCache");
        if (string.IsNullOrWhiteSpace(encryptedCache))
        {
            throw new QuotaException("Claude 桌面版存在，但当前没有可用的登录缓存。");
        }

        try
        {
            byte[] plaintext;
            if (OperatingSystem.IsWindows())
            {
                plaintext = await DecryptWindowsDesktopCacheAsync(dataDirectory, encryptedCache, cancellationToken);
            }
            else if (OperatingSystem.IsMacOS())
            {
                var key = await GetMacDesktopKeyAsync(cancellationToken);
                plaintext = DecryptMacSafeStorage(Convert.FromBase64String(encryptedCache), key);
            }
            else
            {
                throw new QuotaException("当前系统不支持读取 Claude 桌面版登录态。");
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

    /// <summary>Chromium's Windows OSCrypt: a DPAPI-protected AES-256-GCM key in "Local State".</summary>
    [SupportedOSPlatform("windows")]
    private static async Task<byte[]> DecryptWindowsDesktopCacheAsync(
        string dataDirectory,
        string encryptedCache,
        CancellationToken cancellationToken)
    {
        var localStatePath = Path.Combine(dataDirectory, "Local State");
        using var localState = await ReadJsonAsync(localStatePath, "Claude 桌面版安全存储不可用。", cancellationToken);
        var encryptedKey = GetString(localState.RootElement, "os_crypt", "encrypted_key");
        if (string.IsNullOrWhiteSpace(encryptedKey))
        {
            throw new QuotaException("Claude 桌面版存在，但当前没有可用的登录缓存。");
        }

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
        try
        {
            return DecryptElectronSafeStorage(Convert.FromBase64String(encryptedCache), masterKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }

    /// <summary>
    /// Claude Desktop's safe-storage key on macOS, derived once per process from the Keychain
    /// password so an "Allow once" answer does not re-prompt on every refresh.
    /// </summary>
    [SupportedOSPlatform("macos")]
    private static async Task<byte[]> GetMacDesktopKeyAsync(CancellationToken cancellationToken)
    {
        lock (KeychainGate)
        {
            if (_macDesktopKey is not null) return _macDesktopKey;
        }

        var password = await ReadKeychainSecretAsync(
                           ClaudeDesktopKeychainService,
                           ClaudeDesktopKeychainAccount,
                           missingIsError: true,
                           cancellationToken)
                       ?? throw new QuotaException("钥匙串中没有 Claude 桌面版的安全存储密钥，请先打开并登录 Claude 桌面版。");
        try
        {
            var key = DeriveMacSafeStorageKey(password);
            lock (KeychainGate) _macDesktopKey = key;
            return key;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(password);
        }
    }

    /// <summary>
    /// Keychain reads block until the user answers macOS's access prompt. The read runs once in the
    /// background per item; a caller waits at most <see cref="KeychainPromptWait"/> and then gets a
    /// "waiting for approval" error, so the other provider keeps refreshing while the prompt stays
    /// open. Later callers join the same pending read instead of raising a second prompt.
    /// </summary>
    [SupportedOSPlatform("macos")]
    private static async Task<byte[]?> ReadKeychainSecretAsync(
        string service,
        string? account,
        bool missingIsError,
        CancellationToken cancellationToken)
    {
        Task<byte[]?>? pending;
        lock (KeychainGate)
        {
            if (KeychainRetryAfter.TryGetValue(service, out var retryAfter) && DateTimeOffset.Now < retryAfter)
            {
                throw new QuotaException(
                    $"未获准读取钥匙串中的“{service}”。点“立即刷新”可重新请求授权。");
            }

            if (!PendingKeychainReads.TryGetValue(service, out pending))
            {
                pending = Task.Run(() => ReadKeychainSecret(service, account, missingIsError));
                PendingKeychainReads[service] = pending;
            }
        }

        var finished = await Task.WhenAny(pending, Task.Delay(KeychainPromptWait, cancellationToken));
        if (finished != pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new QuotaException(
                $"正在等待钥匙串授权：请在弹出的对话框中允许 Quota Lens 读取“{service}”。");
        }

        lock (KeychainGate)
        {
            if (PendingKeychainReads.TryGetValue(service, out var current) && current == pending)
            {
                PendingKeychainReads.Remove(service);
            }
        }

        return await pending;
    }

    /// <summary>
    /// Reads a generic-password item, honouring the denial back-off. Returns null for a missing item
    /// unless <paramref name="missingIsError"/>; throws <see cref="QuotaException"/> otherwise.
    /// </summary>
    [SupportedOSPlatform("macos")]
    private static byte[]? ReadKeychainSecret(string service, string? account, bool missingIsError)
    {
        lock (KeychainGate)
        {
            if (KeychainRetryAfter.TryGetValue(service, out var retryAfter) && DateTimeOffset.Now < retryAfter)
            {
                throw new QuotaException(
                    $"未获准读取钥匙串中的“{service}”。点“立即刷新”可重新请求授权。");
            }
        }

        var status = MacKeychain.TryReadGenericPassword(service, account, out var secret);
        switch (status)
        {
            case MacKeychain.Success:
                return secret;
            case MacKeychain.ItemNotFound when !missingIsError:
                return null;
            case MacKeychain.ItemNotFound:
                throw new QuotaException("钥匙串中没有 Claude 桌面版的安全存储密钥，请先打开并登录 Claude 桌面版。");
            case MacKeychain.UserCanceled or MacKeychain.AuthFailed:
                lock (KeychainGate) KeychainRetryAfter[service] = DateTimeOffset.Now + KeychainDenialBackoff;
                throw new QuotaException(
                    $"未获准读取钥匙串中的“{service}”。点“立即刷新”可重新请求授权。");
            case MacKeychain.InteractionNotAllowed:
                throw new QuotaException("当前会话无法弹出钥匙串授权，请在已登录的桌面会话中运行 Quota Lens。");
            default:
                throw new QuotaException($"读取钥匙串失败（OSStatus {status}）。");
        }
    }

    /// <summary>Chromium's macOS OSCrypt key: PBKDF2-HMAC-SHA1(password, "saltysalt", 1003) → 16 bytes.</summary>
    internal static byte[] DeriveMacSafeStorageKey(byte[] password) =>
        Rfc2898DeriveBytes.Pbkdf2(password, Encoding.ASCII.GetBytes("saltysalt"), 1003, HashAlgorithmName.SHA1, 16);

    /// <summary>Chromium's macOS OSCrypt payload: "v10" + AES-128-CBC with an IV of 16 spaces.</summary>
    internal static byte[] DecryptMacSafeStorage(byte[] encrypted, byte[] key)
    {
        if (encrypted.Length < 3 + 16 || (encrypted.Length - 3) % 16 != 0)
        {
            throw new CryptographicException("登录缓存长度无效。");
        }

        var prefix = Encoding.ASCII.GetString(encrypted, 0, 3);
        if (prefix != "v10")
        {
            throw new CryptographicException("不支持的登录缓存版本。");
        }

        using var aes = Aes.Create();
        aes.Key = key;
        return aes.DecryptCbc(encrypted.AsSpan(3), MacSafeStorageIv, PaddingMode.PKCS7);
    }

    private static readonly byte[] MacSafeStorageIv = Enumerable.Repeat((byte)' ', 16).ToArray();

    private static string? FindClaudeDesktopDataDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("CLAUDE_DESKTOP_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var expanded = Environment.ExpandEnvironmentVariables(configured);
            if (File.Exists(Path.Combine(expanded, "config.json"))) return expanded;
        }

        if (OperatingSystem.IsMacOS())
        {
            var macCandidate = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library",
                "Application Support",
                "Claude");
            return File.Exists(Path.Combine(macCandidate, "config.json")) ? macCandidate : null;
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
        => FindClaudeDesktopCredential(root)?.AccessToken
           ?? FindStringByPropertyName(root, "token");

    internal static ClaudeCredential? FindClaudeDesktopCredential(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;

        ClaudeCredential? bestCredential = null;
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

            if (bestCredential is null || expiry > bestExpiry)
            {
                bestCredential = new ClaudeCredential(
                    token,
                    GetString(entry.Value, "subscriptionType"),
                    GetString(entry.Value, "rateLimitTier"));
                bestExpiry = expiry;
            }
        }

        return bestCredential;
    }

    private sealed record ClaudeAccountMetadata(string? SubscriptionType, string? RateLimitTier);

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
