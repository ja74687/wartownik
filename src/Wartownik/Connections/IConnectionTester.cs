namespace Wartownik.Connections;

public interface IConnectionTester
{
    Task<ConnectionTestResult> TestAsync(
        ConnectionProfile profile,
        string password,
        CancellationToken cancellationToken = default);
}

public sealed record ConnectionTestResult(bool Success, string? ErrorMessage)
{
    public static ConnectionTestResult Ok() => new(true, null);
    public static ConnectionTestResult Failure(string errorMessage) => new(false, errorMessage);
}
