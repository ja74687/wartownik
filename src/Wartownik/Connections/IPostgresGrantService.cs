namespace Wartownik.Connections;

/// <summary>
/// Reads and applies schema-level privileges for a target role on a given database.
///
/// MVP scope (Iter 5):
/// - Schema-level USAGE/CREATE checked via has_schema_privilege.
/// - Table-class privileges (SELECT/INSERT/UPDATE/DELETE) are tracked via pg_default_acl —
///   i.e. "is this user set up so that future tables in this schema get this privilege?".
///   This matches pgbedrock's "managed schema" model and avoids the partial-state ambiguity
///   you get from has_table_privilege per existing table.
/// - Apply runs every change as one BEGIN/COMMIT batch through the validator.
/// Out of scope here: per-table object overrides, column ACLs, sequence/function privs.
/// </summary>
public interface IPostgresGrantService
{
    Task<IReadOnlyList<SchemaGrantSummary>> ListSchemaGrantsAsync(
        ConnectionProfile profile,
        string profilePassword,
        string databaseName,
        string roleName,
        CancellationToken cancellationToken = default);

    Task ApplyGrantsAsync(
        ConnectionProfile profile,
        string profilePassword,
        string databaseName,
        string roleName,
        IReadOnlyList<GrantChange> changes,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Effective state of a role's privileges on a single schema.
/// Each flag is binary: either the role is fully granted that privilege class on the schema
/// (including default privileges for future objects), or it isn't. Partial states aren't
/// surfaced — see the interface doc for why.
/// </summary>
public sealed record SchemaGrantSummary(
    string SchemaName,
    bool Usage,
    bool Create,
    bool Select,
    bool Insert,
    bool Update,
    bool Delete);

public enum GrantPrivilege
{
    Usage,
    Create,
    Select,
    Insert,
    Update,
    Delete,
}

public enum GrantOperation
{
    Grant,
    Revoke,
}

/// <summary>
/// One pending checkbox flip. The matrix VM produces a list of these and hands them to ApplyGrantsAsync.
/// </summary>
public sealed record GrantChange(
    string SchemaName,
    GrantPrivilege Privilege,
    GrantOperation Operation);
