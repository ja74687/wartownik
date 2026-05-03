using System.Text;
using Wartownik.Postgres;

namespace Wartownik.Connections;

public sealed class PostgresGrantService : IPostgresGrantService
{
    /// <summary>
    /// Per schema: USAGE/CREATE checked directly with has_schema_privilege. The four DML privileges
    /// are answered by pg_default_acl — "does this role get this privilege on every NEW table created in the schema?"
    /// That's the pgbedrock model: managing the future is what makes a schema actually managed.
    /// </summary>
    private const string ListSchemaGrantsSql = """
        WITH user_schemas AS (
            SELECT n.oid, n.nspname
            FROM pg_catalog.pg_namespace n
            WHERE n.nspname NOT IN ('pg_catalog', 'information_schema', 'pg_toast')
              AND n.nspname NOT LIKE 'pg_temp_%'
              AND n.nspname NOT LIKE 'pg_toast_temp_%'
        ),
        role_oid AS (
            SELECT oid FROM pg_catalog.pg_roles WHERE rolname = @roleName
        ),
        defaults_for_role AS (
            -- Each pg_default_acl row is a (grantor, schema, objtype) triplet. Defaults are stored
            -- as aclitem[] entries that look like 'grantee=PRIVS/grantor'. We expand them with
            -- aclexplode and pick the rows that target our role on TABLES (objtype 'r').
            -- aclexplode is referenced as pg_catalog.aclexplode so the SQL validator recognises
            -- it as a system-catalog source.
            SELECT
                d.defaclnamespace AS schema_oid,
                acl.privilege_type AS privilege
            FROM pg_catalog.pg_default_acl d
            JOIN pg_catalog.aclexplode(d.defaclacl) acl ON TRUE
            WHERE d.defaclobjtype = 'r'
              AND acl.grantee = (SELECT oid FROM role_oid)
        )
        SELECT
            s.nspname,
            pg_catalog.has_schema_privilege(@roleName, s.nspname, 'USAGE')  AS has_usage,
            pg_catalog.has_schema_privilege(@roleName, s.nspname, 'CREATE') AS has_create,
            EXISTS(SELECT 1 FROM defaults_for_role d WHERE d.schema_oid = s.oid AND d.privilege = 'SELECT') AS has_select,
            EXISTS(SELECT 1 FROM defaults_for_role d WHERE d.schema_oid = s.oid AND d.privilege = 'INSERT') AS has_insert,
            EXISTS(SELECT 1 FROM defaults_for_role d WHERE d.schema_oid = s.oid AND d.privilege = 'UPDATE') AS has_update,
            EXISTS(SELECT 1 FROM defaults_for_role d WHERE d.schema_oid = s.oid AND d.privilege = 'DELETE') AS has_delete
        FROM user_schemas s
        ORDER BY s.nspname
        """;

    private readonly IPostgresSessionFactory _sessionFactory;

    public PostgresGrantService(IPostgresSessionFactory sessionFactory)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        _sessionFactory = sessionFactory;
    }

    public async Task<IReadOnlyList<SchemaGrantSummary>> ListSchemaGrantsAsync(
        ConnectionProfile profile,
        string profilePassword,
        string databaseName,
        string roleName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(profilePassword);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);

        // Inline the role name as a quoted literal because IPostgresSession.QueryAsync doesn't take
        // parameters yet. Identifiers are validated to a Postgres-safe pattern by the validator at
        // the session edge — this literal is also safely escaped via QuoteLiteral.
        var sql = ListSchemaGrantsSql.Replace("@roleName", QuoteLiteral(roleName));

        var profileForDb = profile with { Database = databaseName };
        await using var session = await _sessionFactory
            .OpenAsync(profileForDb, profilePassword, cancellationToken)
            .ConfigureAwait(false);

        return await session.QueryAsync(
            sql,
            reader => new SchemaGrantSummary(
                SchemaName: reader.GetString(0),
                Usage: reader.GetBoolean(1),
                Create: reader.GetBoolean(2),
                Select: reader.GetBoolean(3),
                Insert: reader.GetBoolean(4),
                Update: reader.GetBoolean(5),
                Delete: reader.GetBoolean(6)),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task ApplyGrantsAsync(
        ConnectionProfile profile,
        string profilePassword,
        string databaseName,
        string roleName,
        IReadOnlyList<GrantChange> changes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(profilePassword);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);
        ArgumentNullException.ThrowIfNull(changes);

        if (changes.Count == 0)
            return;

        var statements = BuildStatements(roleName, changes);

        var profileForDb = profile with { Database = databaseName };
        await using var session = await _sessionFactory
            .OpenAsync(profileForDb, profilePassword, cancellationToken)
            .ConfigureAwait(false);

        await session.ExecuteInTransactionAsync(statements, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the SQL statements for a batch of grant changes. Exposed (internal) so the Preview SQL
    /// modal can render exactly what Apply will run, without re-deriving the rules in the VM.
    /// Each "DML" privilege change emits TWO statements: one for current objects and one for defaults
    /// (so future tables get the same treatment). Schema-level USAGE/CREATE emit a single statement.
    /// </summary>
    internal static IReadOnlyList<string> BuildStatements(string roleName, IReadOnlyList<GrantChange> changes)
    {
        var quotedRole = QuoteIdentifier(roleName);
        var sql = new List<string>(capacity: changes.Count * 2);

        foreach (var change in changes)
        {
            var quotedSchema = QuoteIdentifier(change.SchemaName);
            var verb = change.Operation == GrantOperation.Grant ? "GRANT" : "REVOKE";
            var direction = change.Operation == GrantOperation.Grant ? "TO" : "FROM";

            switch (change.Privilege)
            {
                case GrantPrivilege.Usage:
                    sql.Add($"{verb} USAGE ON SCHEMA {quotedSchema} {direction} {quotedRole}");
                    break;
                case GrantPrivilege.Create:
                    sql.Add($"{verb} CREATE ON SCHEMA {quotedSchema} {direction} {quotedRole}");
                    break;
                case GrantPrivilege.Select:
                case GrantPrivilege.Insert:
                case GrantPrivilege.Update:
                case GrantPrivilege.Delete:
                    var priv = change.Privilege.ToString().ToUpperInvariant();
                    // Apply to all CURRENT tables...
                    sql.Add($"{verb} {priv} ON ALL TABLES IN SCHEMA {quotedSchema} {direction} {quotedRole}");
                    // ...and ensure FUTURE tables get the same treatment via default privileges.
                    sql.Add($"ALTER DEFAULT PRIVILEGES IN SCHEMA {quotedSchema} {verb} {priv} ON TABLES {direction} {quotedRole}");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(changes),
                        $"Unsupported privilege: {change.Privilege}");
            }
        }

        return sql;
    }

    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static string QuoteLiteral(string value) =>
        "'" + value.Replace("'", "''") + "'";
}
