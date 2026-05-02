using System.Data.Common;
using Wartownik.Connections;
using Wartownik.Postgres;

namespace Wartownik.UnitTests.Connections;

public class PostgresMetadataServiceTests
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
    public async Task ListDatabasesAsync_queries_pg_database_with_filters_and_ordering()
    {
        var session = new RecordingSession(new[] { "alpha", "beta" });
        var factory = new FakeFactory(session);
        var sut = new PostgresMetadataService(factory);

        var result = await sut.ListDatabasesAsync(SampleProfile(), "pwd");

        Assert.Equal(new[] { "alpha", "beta" }, result.Select(d => d.Name));
        Assert.Single(session.QueryCalls);
        Assert.Contains("pg_database", session.QueryCalls[0]);
        Assert.Contains("NOT datistemplate", session.QueryCalls[0]);
        Assert.Contains("datallowconn", session.QueryCalls[0]);
        Assert.Contains("ORDER BY datname", session.QueryCalls[0]);
    }

    [Fact]
    public async Task ListDatabasesAsync_disposes_session_after_query()
    {
        var session = new RecordingSession(new[] { "x" });
        var factory = new FakeFactory(session);
        var sut = new PostgresMetadataService(factory);

        await sut.ListDatabasesAsync(SampleProfile(), "pwd");

        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task ListDatabasesAsync_propagates_exceptions_from_factory()
    {
        var factory = new ThrowingFactory(new InvalidOperationException("boom"));
        var sut = new PostgresMetadataService(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ListDatabasesAsync(SampleProfile(), "pwd"));
    }

    [Fact]
    public void Constructor_throws_on_null_factory()
    {
        Assert.Throws<ArgumentNullException>(() => new PostgresMetadataService(null!));
    }

    [Fact]
    public async Task ListDatabasesAsync_throws_on_null_arguments()
    {
        var sut = new PostgresMetadataService(new FakeFactory(new RecordingSession([])));

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.ListDatabasesAsync(null!, "x"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.ListDatabasesAsync(SampleProfile(), null!));
    }

    private sealed class FakeFactory : IPostgresSessionFactory
    {
        private readonly RecordingSession _session;
        public FakeFactory(RecordingSession session) => _session = session;

        public Task<IPostgresSession> OpenAsync(
            ConnectionProfile profile,
            string password,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IPostgresSession>(_session);
    }

    private sealed class ThrowingFactory : IPostgresSessionFactory
    {
        private readonly Exception _ex;
        public ThrowingFactory(Exception ex) => _ex = ex;

        public Task<IPostgresSession> OpenAsync(
            ConnectionProfile profile,
            string password,
            CancellationToken cancellationToken = default) =>
            throw _ex;
    }

    private sealed class RecordingSession : IPostgresSession
    {
        private readonly IReadOnlyList<string> _databaseNames;

        public RecordingSession(IReadOnlyList<string> databaseNames) => _databaseNames = databaseNames;

        public List<string> QueryCalls { get; } = new();
        public bool Disposed { get; private set; }

        public Task<IReadOnlyList<TRow>> QueryAsync<TRow>(
            string sql,
            Func<DbDataReader, TRow> map,
            CancellationToken cancellationToken = default)
        {
            QueryCalls.Add(sql);
            // Return canned data via fake reader
            var rows = new List<TRow>();
            foreach (var name in _databaseNames)
                rows.Add(map(new SingleStringReader(name)));
            return Task.FromResult<IReadOnlyList<TRow>>(rows);
        }

        public Task ExecuteAsync(string sql, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ExecuteInTransactionAsync(IReadOnlyList<string> statements, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SingleStringReader : DbDataReader
    {
        private readonly string _value;
        public SingleStringReader(string value) => _value = value;

        public override string GetString(int ordinal) => _value;

        // Boilerplate — only GetString(0) is exercised by the service.
        public override object this[int ordinal] => throw new NotImplementedException();
        public override object this[string name] => throw new NotImplementedException();
        public override int Depth => 0;
        public override int FieldCount => 1;
        public override bool HasRows => true;
        public override bool IsClosed => false;
        public override int RecordsAffected => -1;
        public override bool GetBoolean(int ordinal) => throw new NotImplementedException();
        public override byte GetByte(int ordinal) => throw new NotImplementedException();
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotImplementedException();
        public override char GetChar(int ordinal) => throw new NotImplementedException();
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotImplementedException();
        public override string GetDataTypeName(int ordinal) => "text";
        public override DateTime GetDateTime(int ordinal) => throw new NotImplementedException();
        public override decimal GetDecimal(int ordinal) => throw new NotImplementedException();
        public override double GetDouble(int ordinal) => throw new NotImplementedException();
        public override Type GetFieldType(int ordinal) => typeof(string);
        public override float GetFloat(int ordinal) => throw new NotImplementedException();
        public override Guid GetGuid(int ordinal) => throw new NotImplementedException();
        public override short GetInt16(int ordinal) => throw new NotImplementedException();
        public override int GetInt32(int ordinal) => throw new NotImplementedException();
        public override long GetInt64(int ordinal) => throw new NotImplementedException();
        public override string GetName(int ordinal) => "datname";
        public override int GetOrdinal(string name) => 0;
        public override object GetValue(int ordinal) => _value;
        public override int GetValues(object[] values) => throw new NotImplementedException();
        public override bool IsDBNull(int ordinal) => false;
        public override bool NextResult() => false;
        public override bool Read() => false;
        public override System.Collections.IEnumerator GetEnumerator() => throw new NotImplementedException();
    }
}
