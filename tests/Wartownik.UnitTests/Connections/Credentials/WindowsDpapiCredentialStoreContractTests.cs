using Wartownik.Connections.Credentials;

namespace Wartownik.UnitTests.Connections.Credentials;

public class WindowsDpapiCredentialStoreContractTests : CredentialStoreContractTests
{
    private string? _filePath;

    protected override bool ShouldSkip(out string reason)
    {
        if (!OperatingSystem.IsWindows())
        {
            reason = "Windows DPAPI is only available on Windows.";
            return true;
        }
        reason = "";
        return false;
    }

    protected override ICredentialStore Create(string serviceName)
    {
        _filePath = Path.Combine(Path.GetTempPath(), $"wartownik-creds-{Guid.NewGuid():N}.json");
#pragma warning disable CA1416
        return new WindowsDpapiCredentialStore(serviceName, _filePath);
#pragma warning restore CA1416
    }

    public override void Dispose()
    {
        base.Dispose();
        if (_filePath is not null)
        {
            try { if (File.Exists(_filePath)) File.Delete(_filePath); } catch { }
            try { if (File.Exists(_filePath + ".tmp")) File.Delete(_filePath + ".tmp"); } catch { }
        }
    }
}
