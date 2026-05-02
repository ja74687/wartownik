using Wartownik.Connections.Credentials;

namespace Wartownik.UnitTests.Connections.Credentials;

public class MacKeychainCredentialStoreContractTests : CredentialStoreContractTests
{
    protected override bool ShouldSkip(out string reason)
    {
        if (!OperatingSystem.IsMacOS())
        {
            reason = "Keychain Services is only available on macOS.";
            return true;
        }
        reason = "";
        return false;
    }

    protected override ICredentialStore Create(string serviceName)
    {
#pragma warning disable CA1416
        return new MacKeychainCredentialStore(serviceName);
#pragma warning restore CA1416
    }
}
