using Wartownik.Connections;

namespace Wartownik.Yaml;

/// <summary>
/// Builds a pgbedrock-compatible YAML snapshot of who's granted what on a database.
/// Scope mirrors the matrix: schema-level USAGE/CREATE plus the four DML privileges
/// (SELECT/INSERT/UPDATE/DELETE) projected from default privileges.
///
/// Out of scope: cluster-level role flags (CREATEDB, REPLICATION, …), table-level
/// overrides, sequence/function privileges. The output is good enough to round-trip
/// through pgbedrock for the things this app actually manages.
/// </summary>
public interface IYamlExporter
{
    Task<string> ExportAsync(
        ConnectionProfile profile,
        string profilePassword,
        string databaseName,
        CancellationToken cancellationToken = default);
}
