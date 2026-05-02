namespace Wartownik.Connections;

public interface IConnectionProfileEditor
{
    Task<ConnectionProfileEditResult?> AddAsync(CancellationToken cancellationToken = default);

    Task<ConnectionProfileEditResult?> EditAsync(
        ConnectionProfile profile,
        string password,
        CancellationToken cancellationToken = default);
}

public sealed record ConnectionProfileEditResult(ConnectionProfile Profile, string Password);
