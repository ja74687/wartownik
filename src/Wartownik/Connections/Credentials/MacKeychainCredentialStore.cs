using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Wartownik.Connections.Credentials;

[SupportedOSPlatform("macos")]
public sealed partial class MacKeychainCredentialStore : ICredentialStore
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const int ErrSecSuccess = 0;
    private const int ErrSecItemNotFound = -25300;
    private const int ErrSecDuplicateItem = -25299;

    private const uint KCFStringEncodingUtf8 = 0x08000100;

    private static readonly Lazy<IntPtr> KSecClass = LoadSecurityConstant("kSecClass");
    private static readonly Lazy<IntPtr> KSecClassGenericPassword = LoadSecurityConstant("kSecClassGenericPassword");
    private static readonly Lazy<IntPtr> KSecAttrService = LoadSecurityConstant("kSecAttrService");
    private static readonly Lazy<IntPtr> KSecAttrAccount = LoadSecurityConstant("kSecAttrAccount");
    private static readonly Lazy<IntPtr> KSecValueData = LoadSecurityConstant("kSecValueData");
    private static readonly Lazy<IntPtr> KSecReturnData = LoadSecurityConstant("kSecReturnData");
    private static readonly Lazy<IntPtr> KSecMatchLimit = LoadSecurityConstant("kSecMatchLimit");
    private static readonly Lazy<IntPtr> KSecMatchLimitOne = LoadSecurityConstant("kSecMatchLimitOne");
    private static readonly Lazy<IntPtr> KCFBooleanTrue = LoadCoreFoundationConstant("kCFBooleanTrue");
    private static readonly Lazy<IntPtr> KCFTypeDictionaryKeyCallBacks = LoadCoreFoundationConstant("kCFTypeDictionaryKeyCallBacks", asAddress: true);
    private static readonly Lazy<IntPtr> KCFTypeDictionaryValueCallBacks = LoadCoreFoundationConstant("kCFTypeDictionaryValueCallBacks", asAddress: true);

    private readonly string _serviceName;

    public MacKeychainCredentialStore(string serviceName)
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("Keychain Services is only available on macOS.");
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        _serviceName = serviceName;
    }

    public string? Get(string key)
    {
        InMemoryCredentialStore.ValidateKey(key);

        var serviceCf = CreateCFString(_serviceName);
        var accountCf = CreateCFString(key);
        IntPtr query = IntPtr.Zero;
        IntPtr resultPtr = IntPtr.Zero;
        try
        {
            query = CreateDictionary(
                (KSecClass.Value, KSecClassGenericPassword.Value),
                (KSecAttrService.Value, serviceCf),
                (KSecAttrAccount.Value, accountCf),
                (KSecReturnData.Value, KCFBooleanTrue.Value),
                (KSecMatchLimit.Value, KSecMatchLimitOne.Value));

            var status = SecItemCopyMatching(query, out resultPtr);
            if (status == ErrSecItemNotFound)
                return null;
            if (status != ErrSecSuccess)
                throw new InvalidOperationException($"Keychain SecItemCopyMatching failed with status {status}.");

            return ReadCFData(resultPtr);
        }
        finally
        {
            if (resultPtr != IntPtr.Zero) CFRelease(resultPtr);
            if (query != IntPtr.Zero) CFRelease(query);
            CFRelease(accountCf);
            CFRelease(serviceCf);
        }
    }

    public void Set(string key, string secret)
    {
        InMemoryCredentialStore.ValidateKey(key);
        ArgumentNullException.ThrowIfNull(secret);

        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var serviceCf = CreateCFString(_serviceName);
        var accountCf = CreateCFString(key);
        var dataCf = CreateCFData(secretBytes);
        IntPtr addDict = IntPtr.Zero;
        IntPtr queryDict = IntPtr.Zero;
        IntPtr updateDict = IntPtr.Zero;
        try
        {
            addDict = CreateDictionary(
                (KSecClass.Value, KSecClassGenericPassword.Value),
                (KSecAttrService.Value, serviceCf),
                (KSecAttrAccount.Value, accountCf),
                (KSecValueData.Value, dataCf));

            var status = SecItemAdd(addDict, IntPtr.Zero);
            if (status == ErrSecSuccess)
                return;

            if (status != ErrSecDuplicateItem)
                throw new InvalidOperationException($"Keychain SecItemAdd failed with status {status}.");

            queryDict = CreateDictionary(
                (KSecClass.Value, KSecClassGenericPassword.Value),
                (KSecAttrService.Value, serviceCf),
                (KSecAttrAccount.Value, accountCf));
            updateDict = CreateDictionary(
                (KSecValueData.Value, dataCf));

            var updateStatus = SecItemUpdate(queryDict, updateDict);
            if (updateStatus != ErrSecSuccess)
                throw new InvalidOperationException($"Keychain SecItemUpdate failed with status {updateStatus}.");
        }
        finally
        {
            if (updateDict != IntPtr.Zero) CFRelease(updateDict);
            if (queryDict != IntPtr.Zero) CFRelease(queryDict);
            if (addDict != IntPtr.Zero) CFRelease(addDict);
            CFRelease(dataCf);
            CFRelease(accountCf);
            CFRelease(serviceCf);
        }
    }

    public bool Remove(string key)
    {
        InMemoryCredentialStore.ValidateKey(key);

        var serviceCf = CreateCFString(_serviceName);
        var accountCf = CreateCFString(key);
        IntPtr query = IntPtr.Zero;
        try
        {
            query = CreateDictionary(
                (KSecClass.Value, KSecClassGenericPassword.Value),
                (KSecAttrService.Value, serviceCf),
                (KSecAttrAccount.Value, accountCf));

            var status = SecItemDelete(query);
            if (status == ErrSecItemNotFound)
                return false;
            if (status != ErrSecSuccess)
                throw new InvalidOperationException($"Keychain SecItemDelete failed with status {status}.");
            return true;
        }
        finally
        {
            if (query != IntPtr.Zero) CFRelease(query);
            CFRelease(accountCf);
            CFRelease(serviceCf);
        }
    }

    private static IntPtr CreateCFString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value + '\0');
        unsafe
        {
            fixed (byte* p = bytes)
            {
                return CFStringCreateWithCString(IntPtr.Zero, (IntPtr)p, KCFStringEncodingUtf8);
            }
        }
    }

    private static IntPtr CreateCFData(byte[] bytes)
    {
        unsafe
        {
            fixed (byte* p = bytes)
            {
                return CFDataCreate(IntPtr.Zero, (IntPtr)p, bytes.Length);
            }
        }
    }

    private static string ReadCFData(IntPtr cfData)
    {
        var length = (int)CFDataGetLength(cfData);
        if (length <= 0)
            return "";

        // An empty CFData can hand back a NULL byte pointer, which Marshal.Copy rejects. That
        // happens for a legitimately stored empty secret — profiles imported from JSON carry no
        // password — so treat it as the empty string rather than letting it throw.
        var ptr = CFDataGetBytePtr(cfData);
        if (ptr == IntPtr.Zero)
            return "";

        var managed = new byte[length];
        Marshal.Copy(ptr, managed, 0, length);
        return Encoding.UTF8.GetString(managed);
    }

    private static IntPtr CreateDictionary(params ReadOnlySpan<(IntPtr Key, IntPtr Value)> pairs)
    {
        var keys = new IntPtr[pairs.Length];
        var values = new IntPtr[pairs.Length];
        for (int i = 0; i < pairs.Length; i++)
        {
            keys[i] = pairs[i].Key;
            values[i] = pairs[i].Value;
        }
        unsafe
        {
            fixed (IntPtr* pKeys = keys)
            fixed (IntPtr* pValues = values)
            {
                return CFDictionaryCreate(
                    IntPtr.Zero,
                    (IntPtr)pKeys,
                    (IntPtr)pValues,
                    pairs.Length,
                    KCFTypeDictionaryKeyCallBacks.Value,
                    KCFTypeDictionaryValueCallBacks.Value);
            }
        }
    }

    // CF "constants" are exported as global variables holding CFTypeRef pointers.
    // We resolve the variable's address via NativeLibrary, then dereference (asAddress=false)
    // to get the CFTypeRef itself. For struct constants like kCFTypeDictionaryKeyCallBacks
    // we want the *address* of the struct (asAddress=true), since the API takes a pointer.
    private static Lazy<IntPtr> LoadSecurityConstant(string symbol) =>
        new(() => LoadConstant(Security, symbol, asAddress: false), LazyThreadSafetyMode.ExecutionAndPublication);

    private static Lazy<IntPtr> LoadCoreFoundationConstant(string symbol, bool asAddress = false) =>
        new(() => LoadConstant(CoreFoundation, symbol, asAddress), LazyThreadSafetyMode.ExecutionAndPublication);

    private static IntPtr LoadConstant(string library, string symbol, bool asAddress)
    {
        var lib = NativeLibrary.Load(library);
        var symbolAddress = NativeLibrary.GetExport(lib, symbol);
        return asAddress ? symbolAddress : Marshal.ReadIntPtr(symbolAddress);
    }

    [LibraryImport(Security)]
    private static partial int SecItemAdd(IntPtr attributes, IntPtr result);

    [LibraryImport(Security)]
    private static partial int SecItemCopyMatching(IntPtr query, out IntPtr result);

    [LibraryImport(Security)]
    private static partial int SecItemUpdate(IntPtr query, IntPtr attributesToUpdate);

    [LibraryImport(Security)]
    private static partial int SecItemDelete(IntPtr query);

    [LibraryImport(CoreFoundation)]
    private static partial IntPtr CFStringCreateWithCString(IntPtr allocator, IntPtr cStr, uint encoding);

    [LibraryImport(CoreFoundation)]
    private static partial IntPtr CFDataCreate(IntPtr allocator, IntPtr bytes, nint length);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFDataGetLength(IntPtr data);

    [LibraryImport(CoreFoundation)]
    private static partial IntPtr CFDataGetBytePtr(IntPtr data);

    [LibraryImport(CoreFoundation)]
    private static partial IntPtr CFDictionaryCreate(
        IntPtr allocator,
        IntPtr keys,
        IntPtr values,
        nint numValues,
        IntPtr keyCallBacks,
        IntPtr valueCallBacks);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(IntPtr cf);
}
