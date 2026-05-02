using System.Data.Common;
using Wartownik.Connections;
using Wartownik.Postgres;

namespace Wartownik.UnitTests.Connections;

public class PostgresConnectionTesterTests
{
    private static ConnectionProfile SampleProfile() =>
        ConnectionProfile.Create(
            displayName: "Sample",
            host: "localhost",
            port: 5432,
            database: "postgres",
            username: "postgres",
            sslMode: PostgresSslMode.Disable);

    [Fact]
    public async Task TestAsync_returns_ok_when_session_opens_successfully()
    {
        var factory = new FakeSessionFactory(_ => Task.FromResult<IPostgresSession>(new FakeSession()));
        var sut = new PostgresConnectionTester(factory);

        var result = await sut.TestAsync(SampleProfile(), "password");

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task TestAsync_returns_failure_with_exception_message_when_open_throws()
    {
        var factory = new FakeSessionFactory(_ => throw new InvalidOperationException("auth failed"));
        var sut = new PostgresConnectionTester(factory);

        var result = await sut.TestAsync(SampleProfile(), "wrong");

        Assert.False(result.Success);
        Assert.Equal("auth failed", result.ErrorMessage);
    }

    [Fact]
    public async Task TestAsync_disposes_session_on_success()
    {
        var session = new FakeSession();
        var factory = new FakeSessionFactory(_ => Task.FromResult<IPostgresSession>(session));
        var sut = new PostgresConnectionTester(factory);

        await sut.TestAsync(SampleProfile(), "password");

        Assert.True(session.Disposed);
    }

    [Fact]
    public void Constructor_throws_on_null_factory()
    {
        Assert.Throws<ArgumentNullException>(() => new PostgresConnectionTester(null!));
    }

    [Fact]
    public async Task TestAsync_throws_on_null_arguments()
    {
        var sut = new PostgresConnectionTester(new FakeSessionFactory(_ => throw new InvalidOperationException()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.TestAsync(null!, "x"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.TestAsync(SampleProfile(), null!));
    }

    private sealed class FakeSessionFactory : IPostgresSessionFactory
    {
        private readonly Func<ConnectionProfile, Task<IPostgresSession>> _open;

        public FakeSessionFactory(Func<ConnectionProfile, Task<IPostgresSession>> open) => _open = open;

        public Task<IPostgresSession> OpenAsync(ConnectionProfile profile, string password, CancellationToken cancellationToken = default)
            => _open(profile);
    }

    private sealed class FakeSession : IPostgresSession
    {
        public bool Disposed { get; private set; }

        public Task<IReadOnlyList<TRow>> QueryAsync<TRow>(string sql, Func<DbDataReader, TRow> map, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TRow>>(Array.Empty<TRow>());

        public Task ExecuteAsync(string sql, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ExecuteInTransactionAsync(IReadOnlyList<string> statements, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
