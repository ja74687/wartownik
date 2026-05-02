using Npgsql;
using Wartownik.Connections;
using Wartownik.Postgres;

namespace Wartownik.IntegrationTests.Postgres;

public class NpgsqlPostgresSessionIntegrationTests
{
    private const string ConnectionStringEnvVar = "WARTOWNIK_PG_CONNECTION";

    private static string? GetConnectionStringOrSkipReason(out string reason)
    {
        var value = Environment.GetEnvironmentVariable(ConnectionStringEnvVar);
        if (string.IsNullOrWhiteSpace(value))
        {
            reason = $"Set {ConnectionStringEnvVar} to a PostgreSQL connection string to enable.";
            return null;
        }
        reason = "";
        return value;
    }

    private static async Task<IPostgresSession> OpenSessionAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return new NpgsqlPostgresSession(connection);
    }

    [SkippableFact]
    public async Task QueryAsync_returns_current_database()
    {
        var connectionString = GetConnectionStringOrSkipReason(out var reason);
        Skip.If(connectionString is null, reason);

        await using var session = await OpenSessionAsync(connectionString!);

        var rows = await session.QueryAsync(
            "SELECT current_database()",
            reader => reader.GetString(0));

        Assert.Single(rows);
        Assert.False(string.IsNullOrEmpty(rows[0]));
    }

    [SkippableFact]
    public async Task ExecuteAsync_runs_no_op_statement()
    {
        var connectionString = GetConnectionStringOrSkipReason(out var reason);
        Skip.If(connectionString is null, reason);

        await using var session = await OpenSessionAsync(connectionString!);

        await session.ExecuteAsync("SET application_name = 'Wartownik-it-exec'");
    }

    [SkippableFact]
    public async Task ExecuteInTransactionAsync_commits_all_statements()
    {
        var connectionString = GetConnectionStringOrSkipReason(out var reason);
        Skip.If(connectionString is null, reason);

        await using var session = await OpenSessionAsync(connectionString!);

        // Use SET LOCAL inside a transaction: harmless and reverts on commit.
        await session.ExecuteInTransactionAsync(new[]
        {
            "SET LOCAL application_name = 'Wartownik-it-tx-1'",
            "SET LOCAL application_name = 'Wartownik-it-tx-2'",
        });
    }

    [SkippableFact]
    public async Task ExecuteInTransactionAsync_rolls_back_on_failure()
    {
        var connectionString = GetConnectionStringOrSkipReason(out var reason);
        Skip.If(connectionString is null, reason);

        var tableName = $"wartownik_it_rollback_{Guid.NewGuid():N}";

        // Pre-create the table outside any transaction so we can verify rollback by counting rows.
        await using (var setup = await OpenSessionAsync(connectionString!))
        {
            await setup.ExecuteAsync($"CREATE TEMP TABLE \"{tableName}\" (n integer)");
            await setup.ExecuteAsync($"INSERT INTO \"{tableName}\" VALUES (1)");

            await Assert.ThrowsAnyAsync<NpgsqlException>(() =>
                setup.ExecuteInTransactionAsync(new[]
                {
                    $"INSERT INTO \"{tableName}\" VALUES (2)",
                    "SELECT 1/0",
                }));

            var rows = await setup.QueryAsync(
                $"SELECT count(*) FROM \"{tableName}\"",
                reader => reader.GetInt64(0));

            Assert.Equal(1, rows[0]);
        }
    }

    [SkippableFact]
    public async Task SessionFactory_opens_with_profile_extracted_from_env_connection_string()
    {
        var connectionString = GetConnectionStringOrSkipReason(out var reason);
        Skip.If(connectionString is null, reason);

        var parsed = new NpgsqlConnectionStringBuilder(connectionString!);
        Skip.If(string.IsNullOrEmpty(parsed.Host), $"{ConnectionStringEnvVar} must include Host.");
        Skip.If(string.IsNullOrEmpty(parsed.Database), $"{ConnectionStringEnvVar} must include Database.");
        Skip.If(string.IsNullOrEmpty(parsed.Username), $"{ConnectionStringEnvVar} must include Username.");
        Skip.If(string.IsNullOrEmpty(parsed.Password), $"{ConnectionStringEnvVar} must include Password.");

        var profile = ConnectionProfile.Create(
            displayName: "Integration test",
            host: parsed.Host!,
            port: parsed.Port,
            database: parsed.Database!,
            username: parsed.Username!,
            sslMode: PostgresSslMode.Prefer);

        var factory = new NpgsqlPostgresSessionFactory(new NpgsqlConnectionStringFactory());
        await using var session = await factory.OpenAsync(profile, parsed.Password!);

        var rows = await session.QueryAsync("SELECT 1", reader => reader.GetInt32(0));
        Assert.Equal(1, rows[0]);
    }
}
