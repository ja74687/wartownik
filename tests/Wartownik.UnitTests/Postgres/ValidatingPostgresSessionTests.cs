using System.Data.Common;
using Wartownik.Postgres;
using Wartownik.Sql;

namespace Wartownik.UnitTests.Postgres;

public class ValidatingPostgresSessionTests
{
    [Fact]
    public async Task QueryAsync_when_validator_allows_calls_inner()
    {
        var inner = new RecordingSession();
        var session = new ValidatingPostgresSession(inner, new StubValidator(allow: true));

        var rows = await session.QueryAsync("SELECT 1", _ => 0);

        Assert.Single(inner.QueryCalls);
        Assert.Equal("SELECT 1", inner.QueryCalls[0]);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task QueryAsync_when_validator_rejects_throws_and_does_not_call_inner()
    {
        var inner = new RecordingSession();
        var session = new ValidatingPostgresSession(
            inner,
            new StubValidator(allow: false, rejectionReason: "nope"));

        var ex = await Assert.ThrowsAsync<SqlNotAllowedException>(
            () => session.QueryAsync("DROP DATABASE prod", _ => 0));

        Assert.Equal("DROP DATABASE prod", ex.Sql);
        Assert.Equal("nope", ex.Reason);
        Assert.Empty(inner.QueryCalls);
    }

    [Fact]
    public async Task ExecuteAsync_when_validator_allows_calls_inner()
    {
        var inner = new RecordingSession();
        var session = new ValidatingPostgresSession(inner, new StubValidator(allow: true));

        await session.ExecuteAsync("CREATE ROLE foo");

        Assert.Single(inner.ExecuteCalls);
        Assert.Equal("CREATE ROLE foo", inner.ExecuteCalls[0]);
    }

    [Fact]
    public async Task ExecuteAsync_when_validator_rejects_throws_and_does_not_call_inner()
    {
        var inner = new RecordingSession();
        var session = new ValidatingPostgresSession(
            inner,
            new StubValidator(allow: false, rejectionReason: "blocked"));

        await Assert.ThrowsAsync<SqlNotAllowedException>(
            () => session.ExecuteAsync("DELETE FROM users"));

        Assert.Empty(inner.ExecuteCalls);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_when_all_allowed_calls_inner_with_all_statements()
    {
        var inner = new RecordingSession();
        var session = new ValidatingPostgresSession(inner, new StubValidator(allow: true));

        var statements = new[] { "CREATE ROLE a", "GRANT SELECT ON t TO a" };
        await session.ExecuteInTransactionAsync(statements);

        Assert.Single(inner.TransactionCalls);
        Assert.Equal(statements, inner.TransactionCalls[0]);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_when_any_rejected_throws_before_calling_inner()
    {
        var inner = new RecordingSession();
        // Reject the second statement only; validate that inner is not invoked at all.
        var validator = new PerStatementValidator(sql => sql != "BAD");
        var session = new ValidatingPostgresSession(inner, validator);

        var statements = new[] { "GOOD", "BAD", "ALSO_GOOD" };

        await Assert.ThrowsAsync<SqlNotAllowedException>(
            () => session.ExecuteInTransactionAsync(statements));

        Assert.Empty(inner.TransactionCalls);
        Assert.Empty(inner.ExecuteCalls);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_throws_on_null_statements()
    {
        var session = new ValidatingPostgresSession(new RecordingSession(), new StubValidator(allow: true));

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => session.ExecuteInTransactionAsync(null!));
    }

    [Fact]
    public async Task DisposeAsync_disposes_inner()
    {
        var inner = new RecordingSession();
        var session = new ValidatingPostgresSession(inner, new StubValidator(allow: true));

        await session.DisposeAsync();

        Assert.True(inner.Disposed);
    }

    [Fact]
    public void Constructor_throws_on_null_inner()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ValidatingPostgresSession(null!, new StubValidator(allow: true)));
    }

    [Fact]
    public void Constructor_throws_on_null_validator()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ValidatingPostgresSession(new RecordingSession(), null!));
    }

    private sealed class StubValidator : ISqlStatementValidator
    {
        private readonly bool _allow;
        private readonly string? _rejectionReason;

        public StubValidator(bool allow, string? rejectionReason = null)
        {
            _allow = allow;
            _rejectionReason = rejectionReason;
        }

        public SqlValidationResult Validate(string sql) =>
            _allow
                ? SqlValidationResult.Allow(SqlStatementCategory.RoleManagement)
                : SqlValidationResult.Reject(_rejectionReason ?? "rejected");
    }

    private sealed class PerStatementValidator : ISqlStatementValidator
    {
        private readonly Func<string, bool> _predicate;

        public PerStatementValidator(Func<string, bool> predicate) => _predicate = predicate;

        public SqlValidationResult Validate(string sql) =>
            _predicate(sql)
                ? SqlValidationResult.Allow(SqlStatementCategory.RoleManagement)
                : SqlValidationResult.Reject($"rejected: {sql}");
    }

    private sealed class RecordingSession : IPostgresSession
    {
        public List<string> QueryCalls { get; } = new();
        public List<string> ExecuteCalls { get; } = new();
        public List<IReadOnlyList<string>> TransactionCalls { get; } = new();
        public bool Disposed { get; private set; }

        public Task<IReadOnlyList<TRow>> QueryAsync<TRow>(
            string sql,
            Func<DbDataReader, TRow> map,
            CancellationToken cancellationToken = default)
        {
            QueryCalls.Add(sql);
            return Task.FromResult<IReadOnlyList<TRow>>(Array.Empty<TRow>());
        }

        public Task ExecuteAsync(string sql, CancellationToken cancellationToken = default)
        {
            ExecuteCalls.Add(sql);
            return Task.CompletedTask;
        }

        public Task ExecuteInTransactionAsync(
            IReadOnlyList<string> statements,
            CancellationToken cancellationToken = default)
        {
            TransactionCalls.Add(statements);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
