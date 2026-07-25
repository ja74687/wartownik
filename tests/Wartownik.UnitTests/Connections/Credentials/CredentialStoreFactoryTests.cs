using System.Runtime.Versioning;
using Wartownik.Connections.Credentials;

namespace Wartownik.UnitTests.Connections.Credentials;

public class CredentialStoreFactoryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateForCurrentPlatform_throws_on_blank_service_name(string? serviceName)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            CredentialStoreFactory.CreateForCurrentPlatform(serviceName!));
    }

    [SkippableFact]
    [SupportedOSPlatform("windows")]
    public void CreateForCurrentPlatform_returns_DPAPI_store_on_Windows()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only check.");
        var store = CredentialStoreFactory.CreateForCurrentPlatform("WartownikTest");
        Assert.IsType<WindowsDpapiCredentialStore>(store);
    }

    [SkippableFact]
    [SupportedOSPlatform("linux")]
    public void CreateForCurrentPlatform_returns_libsecret_store_on_Linux()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux-only check.");
        var store = CredentialStoreFactory.CreateForCurrentPlatform("WartownikTest");
        Assert.IsType<LinuxSecretServiceCredentialStore>(store);
    }

    [SkippableFact]
    [SupportedOSPlatform("macos")]
    public void CreateForCurrentPlatform_returns_keychain_store_on_macOS()
    {
        Skip.IfNot(OperatingSystem.IsMacOS(), "macOS-only check.");
        var store = CredentialStoreFactory.CreateForCurrentPlatform("WartownikTest");
        Assert.IsType<MacKeychainCredentialStore>(store);
    }
}
