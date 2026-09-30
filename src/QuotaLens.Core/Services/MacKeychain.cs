using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace QuotaLens.Services;

/// <summary>
/// Minimal read access to generic-password items in the login keychain through Security.framework.
/// Calling the framework directly (rather than the <c>security</c> tool) makes macOS name
/// Quota Lens in the access prompt, and "Always Allow" then trusts only this app, not every
/// process that can run <c>security</c>.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacKeychain
{
    internal const int Success = 0;
    internal const int UserCanceled = -128;
    internal const int AuthFailed = -25293;
    internal const int ItemNotFound = -25300;
    internal const int InteractionNotAllowed = -25308;

    private const string SecurityFramework = "/System/Library/Frameworks/Security.framework/Security";

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainFindGenericPassword(
        IntPtr keychainOrArray,
        uint serviceNameLength,
        byte[] serviceName,
        uint accountNameLength,
        byte[]? accountName,
        out uint passwordLength,
        out IntPtr passwordData,
        IntPtr itemRef);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

    /// <summary>
    /// Blocks while macOS shows its access prompt. A null <paramref name="account"/> matches any
    /// account. Returns the OSStatus; <paramref name="secret"/> is empty unless it is
    /// <see cref="Success"/>.
    /// </summary>
    internal static int TryReadGenericPassword(string service, string? account, out byte[] secret)
    {
        var serviceBytes = Encoding.UTF8.GetBytes(service);
        var accountBytes = account is null ? null : Encoding.UTF8.GetBytes(account);
        var status = SecKeychainFindGenericPassword(
            IntPtr.Zero,
            (uint)serviceBytes.Length,
            serviceBytes,
            (uint)(accountBytes?.Length ?? 0),
            accountBytes,
            out var length,
            out var data,
            IntPtr.Zero);

        if (status != Success || data == IntPtr.Zero)
        {
            secret = Array.Empty<byte>();
            return status == Success ? ItemNotFound : status;
        }

        try
        {
            secret = new byte[length];
            Marshal.Copy(data, secret, 0, (int)length);
            return Success;
        }
        finally
        {
            SecKeychainItemFreeContent(IntPtr.Zero, data);
        }
    }
}
