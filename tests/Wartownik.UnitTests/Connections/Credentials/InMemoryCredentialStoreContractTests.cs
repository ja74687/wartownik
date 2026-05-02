using Wartownik.Connections.Credentials;

namespace Wartownik.UnitTests.Connections.Credentials;

public class InMemoryCredentialStoreContractTests : CredentialStoreContractTests
{
    protected override bool ShouldSkip(out string reason)
    {
        reason = "";
        return false;
    }

    protected override ICredentialStore Create(string serviceName) => new InMemoryCredentialStore();
}
