using Wartownik.Postgres;

namespace Wartownik.Connections;

public sealed class PostgresMetadataService : IPostgresMetadataService
{
    private const string ListDatabasesSql =
        "SELECT datname FROM pg_database WHERE NOT datistemplate AND datallowconn ORDER BY datname";

    private readonly IPostgresSessionFactory _sessionFactory;

    public PostgresMetadataService(IPostgresSessionFactory sessionFactory)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        _sessionFactory = sessionFactory;
    }

    public async Task<IReadOnlyList<DatabaseSummary>> ListDatabasesAsync(
        ConnectionProfile profile,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(password);

        await using var session = await _sessionFactory
            .OpenAsync(profile, password, cancellationToken)
            .ConfigureAwait(false);

        return await session.QueryAsync(
            ListDatabasesSql,
            reader => new DatabaseSummary(reader.GetString(0)),
            cancellationToken).ConfigureAwait(false);
    }
}
