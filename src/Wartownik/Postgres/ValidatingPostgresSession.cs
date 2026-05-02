using System.Data.Common;
using Wartownik.Sql;

namespace Wartownik.Postgres;

public sealed class ValidatingPostgresSession : IPostgresSession
{
    private readonly IPostgresSession _inner;
    private readonly ISqlStatementValidator _validator;

    public ValidatingPostgresSession(IPostgresSession inner, ISqlStatementValidator validator)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(validator);
        _inner = inner;
        _validator = validator;
    }

    public Task<IReadOnlyList<TRow>> QueryAsync<TRow>(
        string sql,
        Func<DbDataReader, TRow> map,
        CancellationToken cancellationToken = default)
    {
        EnsureAllowed(sql);
        return _inner.QueryAsync(sql, map, cancellationToken);
    }

    public Task ExecuteAsync(string sql, CancellationToken cancellationToken = default)
    {
        EnsureAllowed(sql);
        return _inner.ExecuteAsync(sql, cancellationToken);
    }

    public Task ExecuteInTransactionAsync(
        IReadOnlyList<string> statements,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(statements);
        foreach (var sql in statements)
            EnsureAllowed(sql);
        return _inner.ExecuteInTransactionAsync(statements, cancellationToken);
    }

    public ValueTask DisposeAsync() => _inner.DisposeAsync();

    private void EnsureAllowed(string sql)
    {
        var result = _validator.Validate(sql);
        if (!result.IsAllowed)
            throw new SqlNotAllowedException(sql, result.RejectionReason ?? "Statement was rejected.");
    }
}
