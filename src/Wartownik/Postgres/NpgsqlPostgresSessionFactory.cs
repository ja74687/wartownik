using Npgsql;
using Wartownik.Connections;

namespace Wartownik.Postgres;

public sealed class NpgsqlPostgresSessionFactory : IPostgresSessionFactory
{
    private readonly NpgsqlConnectionStringFactory _connectionStringFactory;

    public NpgsqlPostgresSessionFactory(NpgsqlConnectionStringFactory connectionStringFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionStringFactory);
        _connectionStringFactory = connectionStringFactory;
    }

    public async Task<IPostgresSession> OpenAsync(
        ConnectionProfile profile,
        string password,
        CancellationToken cancellationToken = default)
    {
        var connectionString = _connectionStringFactory.Build(profile, password);
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return new NpgsqlPostgresSession(connection);
    }
}
