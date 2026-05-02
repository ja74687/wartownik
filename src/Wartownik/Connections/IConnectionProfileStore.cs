namespace Wartownik.Connections;

public interface IConnectionProfileStore
{
    Task<IReadOnlyList<ConnectionProfile>> ListAsync(CancellationToken cancellationToken = default);

    Task<ConnectionProfile?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
