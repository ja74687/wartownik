namespace Wartownik.Connections;

public interface IConnectionProfileService
{
    Task<IReadOnlyList<ConnectionProfile>> ListAsync(CancellationToken cancellationToken = default);

    Task<ConnectionProfile?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<string?> GetPasswordAsync(Guid id, CancellationToken cancellationToken = default);

    Task SaveAsync(ConnectionProfile profile, string password, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
