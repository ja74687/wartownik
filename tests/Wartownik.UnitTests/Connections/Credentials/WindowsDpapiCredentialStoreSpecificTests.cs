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
#pragma warning disable CA1416
        Assert.Throws<PlatformNotSupportedException>(() =>
            new WindowsDpapiCredentialStore("svc", _filePath));
#pragma warning restore CA1416
    }

    [SkippableFact]
    public void Stored_secret_is_not_present_in_plaintext_on_disk()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test.");
#pragma warning disable CA1416
        var store = new WindowsDpapiCredentialStore("svc", _filePath);
#pragma warning restore CA1416

        const string secret = "topsecret-2026-payload";
        store.Set("k", secret);

        var contents = File.ReadAllText(_filePath, Encoding.UTF8);
        Assert.DoesNotContain(secret, contents, StringComparison.Ordinal);
    }

    [SkippableFact]
    public void Different_service_name_cannot_decrypt_secret()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test.");
#pragma warning disable CA1416
        var storeA = new WindowsDpapiCredentialStore("service-A", _filePath);
        storeA.Set("k", "secret");

        var storeB = new WindowsDpapiCredentialStore("service-B", _filePath);
#pragma warning restore CA1416

        // Different entropy → DPAPI throws CryptographicException on Unprotect.
        Assert.ThrowsAny<Exception>(() => storeB.Get("k"));
    }

    [SkippableFact]
    public void Atomic_write_does_not_leave_temp_file()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test.");
#pragma warning disable CA1416
        var store = new WindowsDpapiCredentialStore("svc", _filePath);
#pragma warning restore CA1416

        store.Set("k", "v");

        Assert.False(File.Exists(_filePath + ".tmp"));
    }
}
