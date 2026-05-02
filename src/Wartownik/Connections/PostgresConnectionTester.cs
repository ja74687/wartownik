using Wartownik.Postgres;

namespace Wartownik.Connections;

public sealed class PostgresConnectionTester : IConnectionTester
{
    private readonly IPostgresSessionFactory _sessionFactory;

    public PostgresConnectionTester(IPostgresSessionFactory sessionFactory)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        _sessionFactory = sessionFactory;
    }

    public async Task<ConnectionTestResult> TestAsync(
        ConnectionProfile profile,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(password);

        try
        {
            await using var session = await _sessionFactory
                .OpenAsync(profile, password, cancellationToken)
                .ConfigureAwait(false);
            return ConnectionTestResult.Ok();
        }
        catch (Exception ex)
        {
            return ConnectionTestResult.Failure(ex.Message);
        }
    }
}
