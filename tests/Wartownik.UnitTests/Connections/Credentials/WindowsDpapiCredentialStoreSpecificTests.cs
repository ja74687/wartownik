using System.Runtime.Versioning;
using System.Text;
using Wartownik.Connections.Credentials;

namespace Wartownik.UnitTests.Connections.Credentials;

public class WindowsDpapiCredentialStoreSpecificTests : IDisposable
{
    private readonly string _filePath = Path.Combine(
        Path.GetTempPath(), $"wartownik-dpapi-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try { if (File.Exists(_filePath)) File.Delete(_filePath); } catch { }
        try { if (File.Exists(_filePath + ".tmp")) File.Delete(_filePath + ".tmp"); } catch { }
        GC.SuppressFinalize(this);
    }

    [SkippableFact]
    public void Constructor_throws_on_non_Windows()
    {
        Skip.If(OperatingSystem.IsWindows(), "Negative test only meaningful off Windows.");
        // Deliberately calling a Windows-only API from a non-Windows run — that refusal is the
        // whole point of the test, so the platform check has to be suppressed rather than declared.
#pragma warning disable CA1416
        Assert.Throws<PlatformNotSupportedException>(() =>
            new WindowsDpapiCredentialStore("svc", _filePath));
#pragma warning restore CA1416
    }

    [SkippableFact]
    [SupportedOSPlatform("windows")]
    public void Stored_secret_is_not_present_in_plaintext_on_disk()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test.");
        var store = new WindowsDpapiCredentialStore("svc", _filePath);

        const string secret = "topsecret-2026-payload";
        store.Set("k", secret);

        var contents = File.ReadAllText(_filePath, Encoding.UTF8);
        Assert.DoesNotContain(secret, contents, StringComparison.Ordinal);
    }

    [SkippableFact]
    [SupportedOSPlatform("windows")]
    public void Different_service_name_cannot_decrypt_secret()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test.");
        var storeA = new WindowsDpapiCredentialStore("service-A", _filePath);
        storeA.Set("k", "secret");

        var storeB = new WindowsDpapiCredentialStore("service-B", _filePath);

        // Different entropy → DPAPI throws CryptographicException on Unprotect.
        Assert.ThrowsAny<Exception>(() => storeB.Get("k"));
    }

    [SkippableFact]
    [SupportedOSPlatform("windows")]
    public void Atomic_write_does_not_leave_temp_file()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test.");
        var store = new WindowsDpapiCredentialStore("svc", _filePath);

        store.Set("k", "v");

        Assert.False(File.Exists(_filePath + ".tmp"));
    }
}
