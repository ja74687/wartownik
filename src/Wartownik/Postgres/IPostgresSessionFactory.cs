using Wartownik.Connections;

namespace Wartownik.Postgres;

public interface IPostgresSessionFactory
{
    Task<IPostgresSession> OpenAsync(
        ConnectionProfile profile,
        string password,
        CancellationToken cancellationToken = default);
}
