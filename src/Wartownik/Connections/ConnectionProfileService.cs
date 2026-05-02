using Wartownik.Connections.Credentials;

namespace Wartownik.Connections;

public sealed class ConnectionProfileService : IConnectionProfileService
{
    private readonly IConnectionProfileStore _profileStore;
    private readonly ICredentialStore _credentialStore;

    public ConnectionProfileService(
        IConnectionProfileStore profileStore,
        ICredentialStore credentialStore)
    {
        ArgumentNullException.ThrowIfNull(profileStore);
        ArgumentNullException.ThrowIfNull(credentialStore);
        _profileStore = profileStore;
        _credentialStore = credentialStore;
    }

    public Task<IReadOnlyList<ConnectionProfile>> ListAsync(CancellationToken cancellationToken = default) =>
        _profileStore.ListAsync(cancellationToken);

    public Task<ConnectionProfile?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        _profileStore.GetAsync(id, cancellationToken);

    public Task<string?> GetPasswordAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var key = CredentialKey(id);
        return Task.FromResult(_credentialStore.Get(key));
    }

    public async Task SaveAsync(
        ConnectionProfile profile,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(password);

        await _profileStore.SaveAsync(profile, cancellationToken).ConfigureAwait(false);
        _credentialStore.Set(CredentialKey(profile.Id), password);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var removed = await _profileStore.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        _credentialStore.Remove(CredentialKey(id));
        return removed;
    }

    private static string CredentialKey(Guid id) => $"profile:{id:N}";
}
