using System.Data.Common;

namespace Wartownik.Postgres;

public interface IPostgresSession : IAsyncDisposable
{
    Task<IReadOnlyList<TRow>> QueryAsync<TRow>(
        string sql,
        Func<DbDataReader, TRow> map,
        CancellationToken cancellationToken = default);

    Task ExecuteAsync(
        string sql,
        CancellationToken cancellationToken = default);

    Task ExecuteInTransactionAsync(
        IReadOnlyList<string> statements,
        CancellationToken cancellationToken = default);
}
