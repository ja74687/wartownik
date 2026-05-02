using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Wartownik.Connections.Credentials;

[SupportedOSPlatform("linux")]
public sealed partial class LinuxSecretServiceCredentialStore : ICredentialStore
{
    private const string LibSecret = "libsecret-1.so.0";
    private const string LibGObject = "libgobject-2.0.so.0";

    private const int SecretSchemaFlagsNone = 0;
    private const int SecretSchemaAttributeString = 0;

    private const string AttrService = "service";
    private const string AttrAccount = "account";

    private static readonly Lazy<IntPtr> SchemaHandle =
        new(CreateSchema, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly string _serviceName;

    public LinuxSecretServiceCredentialStore(string serviceName)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("libsecret is only available on Linux.");
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        _serviceName = serviceName;
    }

    public string? Get(string key)
    {
        InMemoryCredentialStore.ValidateKey(key);

        var passwordPtr = secret_password_lookup_sync(
            SchemaHandle.Value,
            IntPtr.Zero,
            out var errorPtr,
            AttrService, _serviceName,
            AttrAccount, key,
            IntPtr.Zero);

        ThrowIfError(errorPtr);

        if (passwordPtr == IntPtr.Zero)
            return null;

        try
        {
            return Marshal.PtrToStringUTF8(passwordPtr);
        }
        finally
        {
            secret_password_free(passwordPtr);
        }
    }

    public void Set(string key, string secret)
    {
        InMemoryCredentialStore.ValidateKey(key);
        ArgumentNullException.ThrowIfNull(secret);

        var ok = secret_password_store_sync(
            SchemaHandle.Value,
            collection: null,
            label: $"{_serviceName}:{key}",
            password: secret,
            IntPtr.Zero,
            out var errorPtr,
            AttrService, _serviceName,
            AttrAccount, key,
            IntPtr.Zero);

        ThrowIfError(errorPtr);

        if (!ok)
            throw new InvalidOperationException("libsecret refused to store password (no detailed error).");
    }

    public bool Remove(string key)
    {
        InMemoryCredentialStore.ValidateKey(key);

        var removed = secret_password_clear_sync(
            SchemaHandle.Value,
            IntPtr.Zero,
            out var errorPtr,
            AttrService, _serviceName,
            AttrAccount, key,
            IntPtr.Zero);

        ThrowIfError(errorPtr);
        return removed;
    }

    private static IntPtr CreateSchema()
    {
        return secret_schema_new(
            "com.softime.Wartownik",
            SecretSchemaFlagsNone,
            AttrService, SecretSchemaAttributeString,
            AttrAccount, SecretSchemaAttributeString,
            IntPtr.Zero);
    }

    private static void ThrowIfError(IntPtr errorPtr)
    {
        if (errorPtr == IntPtr.Zero) return;

        // GError layout: GQuark domain (4) + gint code (4) + gchar* message (ptr-sized, aligned).
        var messagePtr = Marshal.ReadIntPtr(errorPtr, IntPtr.Size);
        var message = Marshal.PtrToStringUTF8(messagePtr) ?? "<no message>";
        g_error_free(errorPtr);
        throw new InvalidOperationException($"libsecret error: {message}");
    }

    [LibraryImport(LibSecret, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr secret_schema_new(
        string name,
        int flags,
        string attr1Name, int attr1Type,
        string attr2Name, int attr2Type,
        IntPtr terminator);

    [LibraryImport(LibSecret, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool secret_password_store_sync(
        IntPtr schema,
        string? collection,
        string label,
        string password,
        IntPtr cancellable,
        out IntPtr error,
        string attr1Name, string attr1Value,
        string attr2Name, string attr2Value,
        IntPtr terminator);

    [LibraryImport(LibSecret, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr secret_password_lookup_sync(
        IntPtr schema,
        IntPtr cancellable,
        out IntPtr error,
        string attr1Name, string attr1Value,
        string attr2Name, string attr2Value,
        IntPtr terminator);

    [LibraryImport(LibSecret, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool secret_password_clear_sync(
        IntPtr schema,
        IntPtr cancellable,
        out IntPtr error,
        string attr1Name, string attr1Value,
        string attr2Name, string attr2Value,
        IntPtr terminator);

    [LibraryImport(LibSecret)]
    private static partial void secret_password_free(IntPtr password);

    [LibraryImport(LibGObject)]
    private static partial void g_error_free(IntPtr error);
}
