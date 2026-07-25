using Wartownik.Connections.Credentials;

namespace Wartownik.UnitTests.Connections.Credentials;

public class LinuxSecretServiceCredentialStoreContractTests : CredentialStoreContractTests
{
    /// <summary>
    /// Why the contract can't be exercised here, or null when it can. Being on Linux isn't
    /// enough: libsecret is a native dependency that plenty of machines don't have — headless CI
    /// runners and minimal containers among them — and it needs a Secret Service on the session
    /// bus to talk to. Probing once tells us which, so the suite skips with a real reason instead
    /// of failing with a DllNotFoundException.
    /// </summary>
    private static readonly Lazy<string?> SkipReason = new(() =>
    {
        if (!OperatingSystem.IsLinux())
            return "libsecret is only available on Linux.";

        try
        {
#pragma warning disable CA1416
            var probe = new LinuxSecretServiceCredentialStore($"WartownikProbe-{Guid.NewGuid():N}");
            // Forces the native library to load and the schema to be created — the store defers
            // both until the first real call, so constructing it alone proves nothing.
            probe.Get("probe");
#pragma warning restore CA1416
            return null;
        }
        catch (DllNotFoundException)
        {
            return "libsecret is not installed on this machine.";
        }
        catch (Exception ex)
        {
            // Typically no Secret Service on the session bus (headless, no keyring daemon).
            return $"libsecret is unusable here ({ex.GetType().Name}).";
        }
    });

    protected override bool ShouldSkip(out string reason)
    {
        reason = SkipReason.Value ?? "";
        return reason.Length > 0;
    }

    protected override ICredentialStore Create(string serviceName)
    {
#pragma warning disable CA1416
        return new LinuxSecretServiceCredentialStore(serviceName);
#pragma warning restore CA1416
    }
}
