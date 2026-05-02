using System.Data.Common;
using Npgsql;

namespace Wartownik.Postgres;

public sealed class NpgsqlPostgresSession : IPostgresSession
{
    private readonly NpgsqlConnection _connection;
    private bool _disposed;

    public NpgsqlPostgresSession(NpgsqlConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connection = connection;
    }

    public async Task<IReadOnlyList<TRow>> QueryAsync<TRow>(
        string sql,
        Func<DbDataReader, TRow> map,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sql);
        ArgumentNullException.ThrowIfNull(map);
        ThrowIfDisposed();

        await using var command = new NpgsqlCommand(sql, _connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var rows = new List<TRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            rows.Add(map(reader));

        return rows;
    }

    public async Task ExecuteAsync(string sql, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sql);
        ThrowIfDisposed();

        await using var command = new NpgsqlCommand(sql, _connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ExecuteInTransactionAsync(
        IReadOnlyList<string> statements,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(statements);
        if (statements.Count == 0)
            throw new ArgumentException("At least one statement is required.", nameof(statements));
        ThrowIfDisposed();

        await using var transaction = await _connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var sql in statements)
        {
            if (string.IsNullOrEmpty(sql))
                throw new ArgumentException("Statements must not contain empty entries.", nameof(statements));

            await using var command = new NpgsqlCommand(sql, _connection, transaction);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(NpgsqlPostgresSession));
    }
}
