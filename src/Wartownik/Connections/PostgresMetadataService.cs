using Wartownik.Postgres;

namespace Wartownik.Connections;

public sealed class PostgresMetadataService : IPostgresMetadataService
{
    private const string ListDatabasesSql = """
        SELECT
            d.datname,
            pg_catalog.pg_get_userbyid(d.datdba) AS owner,
            CASE WHEN pg_catalog.has_database_privilege(d.datname, 'CONNECT')
                 THEN pg_catalog.pg_database_size(d.datname)
                 ELSE NULL END AS size_bytes,
            current_setting('server_version') AS server_version
        FROM pg_catalog.pg_database d
        WHERE NOT d.datistemplate AND d.datallowconn
        ORDER BY d.datname
        """;

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
            reader => new DatabaseSummary(
                Name: reader.GetString(0),
                Owner: reader.IsDBNull(1) ? null : reader.GetString(1),
                ServerVersion: reader.IsDBNull(3) ? null : reader.GetString(3),
                SizeBytes: reader.IsDBNull(2) ? null : reader.GetInt64(2)),
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
