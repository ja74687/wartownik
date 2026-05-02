using Wartownik.Connections;
using Wartownik.Sql;

namespace Wartownik.Postgres;

public sealed class ValidatingPostgresSessionFactory : IPostgresSessionFactory
{
    private readonly IPostgresSessionFactory _inner;
    private readonly ISqlStatementValidator _validator;

    public ValidatingPostgresSessionFactory(IPostgresSessionFactory inner, ISqlStatementValidator validator)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(validator);
        _inner = inner;
        _validator = validator;
    }

    public async Task<IPostgresSession> OpenAsync(
        ConnectionProfile profile,
        string password,
        CancellationToken cancellationToken = default)
    {
        var session = await _inner.OpenAsync(profile, password, cancellationToken).ConfigureAwait(false);
        return new ValidatingPostgresSession(session, _validator);
    }
}
