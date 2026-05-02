using System.Runtime.InteropServices;

namespace Wartownik.Connections.Credentials;

public static class CredentialStoreFactory
{
    public const string DefaultServiceName = "Wartownik";

    public static ICredentialStore CreateForCurrentPlatform(string serviceName = DefaultServiceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        if (OperatingSystem.IsWindows())
            return new WindowsDpapiCredentialStore(serviceName);

        if (OperatingSystem.IsLinux())
            return new LinuxSecretServiceCredentialStore(serviceName);

        if (OperatingSystem.IsMacOS())
            return new MacKeychainCredentialStore(serviceName);

        throw new PlatformNotSupportedException(
            $"No credential store implementation for this OS: {RuntimeInformation.OSDescription}");
    }
}
