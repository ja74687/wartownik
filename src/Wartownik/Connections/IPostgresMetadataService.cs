namespace Wartownik.Connections;

public interface IPostgresMetadataService
{
    Task<IReadOnlyList<DatabaseSummary>> ListDatabasesAsync(
        ConnectionProfile profile,
        string password,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RoleSummary>> ListRolesAsync(
        ConnectionProfile profile,
        string password,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SchemaSummary>> ListSchemasAsync(
        ConnectionProfile profile,
        string password,
        string databaseName,
        CancellationToken cancellationToken = default);
}

public sealed record DatabaseSummary(string Name);

public sealed record RoleSummary(
    string Name,
    bool IsSuperuser,
    bool CanCreateDb,
    bool CanCreateRole,
    bool CanLogin);

public sealed record SchemaSummary(string Name);
