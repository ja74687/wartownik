namespace Wartownik.Connections;

public interface IPostgresMetadataService
{
    Task<IReadOnlyList<DatabaseSummary>> ListDatabasesAsync(
        ConnectionProfile profile,
        string password,
        CancellationToken cancellationToken = default);
}

public sealed record DatabaseSummary(string Name);
