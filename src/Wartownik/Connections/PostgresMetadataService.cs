using Wartownik.Postgres;

namespace Wartownik.Connections;

public sealed class PostgresMetadataService : IPostgresMetadataService
{
    private const string ListDatabasesSql =
        "SELECT datname FROM pg_database WHERE NOT datistemplate AND datallowconn ORDER BY datname";

    private const string ListRolesSql =
        "SELECT rolname, rolsuper, rolcreatedb, rolcreaterole, rolcanlogin FROM pg_roles ORDER BY rolname";

    private const string ListSchemasSql =
        "SELECT schema_name FROM information_schema.schemata " +
        "WHERE schema_name NOT IN ('pg_catalog', 'information_schema', 'pg_toast') " +
        "ORDER BY schema_name";

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

    public async Task<IReadOnlyList<RoleSummary>> ListRolesAsync(
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
            ListRolesSql,
            reader => new RoleSummary(
                Name: reader.GetString(0),
                IsSuperuser: reader.GetBoolean(1),
                CanCreateDb: reader.GetBoolean(2),
                CanCreateRole: reader.GetBoolean(3),
                CanLogin: reader.GetBoolean(4)),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SchemaSummary>> ListSchemasAsync(
        ConnectionProfile profile,
        string password,
        string databaseName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(password);
        if (string.IsNullOrWhiteSpace(databaseName))
            throw new ArgumentException("Database name must not be blank.", nameof(databaseName));

        var profileForDatabase = profile with { Database = databaseName };

        await using var session = await _sessionFactory
            .OpenAsync(profileForDatabase, password, cancellationToken)
            .ConfigureAwait(false);

        return await session.QueryAsync(
            ListSchemasSql,
            reader => new SchemaSummary(reader.GetString(0)),
            cancellationToken).ConfigureAwait(false);
    }
}
