using Wartownik.Connections.Credentials;

namespace Wartownik.UnitTests.Connections.Credentials;

public class LinuxSecretServiceCredentialStoreContractTests : CredentialStoreContractTests
{
    protected override bool ShouldSkip(out string reason)
    {
        if (!OperatingSystem.IsLinux())
        {
            reason = "libsecret is only available on Linux.";
            return true;
        }
        reason = "";
        return false;
    }

    protected override ICredentialStore Create(string serviceName)
    {
#pragma warning disable CA1416
        return new LinuxSecretServiceCredentialStore(serviceName);
#pragma warning restore CA1416
    }
}
