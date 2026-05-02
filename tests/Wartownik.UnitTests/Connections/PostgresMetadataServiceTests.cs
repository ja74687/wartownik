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
        var session = new RecordingSession(databases: new[] { "alpha", "beta" });
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
        var session = new RecordingSession(databases: new[] { "x" });
        var factory = new FakeFactory(session);
        var sut = new PostgresMetadataService(factory);

        await sut.ListDatabasesAsync(SampleProfile(), "pwd");

        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task ListRolesAsync_queries_pg_roles_with_ordering()
    {
        var session = new RecordingSession(roles: new[]
        {
            new RoleSummary("admin", IsSuperuser: true, CanCreateDb: true, CanCreateRole: true, CanLogin: true),
            new RoleSummary("readonly", IsSuperuser: false, CanCreateDb: false, CanCreateRole: false, CanLogin: false),
        });
        var sut = new PostgresMetadataService(new FakeFactory(session));

        var result = await sut.ListRolesAsync(SampleProfile(), "pwd");

        Assert.Equal(2, result.Count);
        Assert.Equal("admin", result[0].Name);
        Assert.True(result[0].IsSuperuser);
        Assert.True(result[0].CanCreateDb);
        Assert.True(result[0].CanCreateRole);
        Assert.True(result[0].CanLogin);
        Assert.Equal("readonly", result[1].Name);
        Assert.False(result[1].IsSuperuser);
        Assert.False(result[1].CanCreateDb);
        Assert.False(result[1].CanCreateRole);
        Assert.False(result[1].CanLogin);

        Assert.Single(session.QueryCalls);
        Assert.Contains("pg_roles", session.QueryCalls[0]);
        Assert.Contains("rolname", session.QueryCalls[0]);
        Assert.Contains("rolsuper", session.QueryCalls[0]);
        Assert.Contains("rolcreatedb", session.QueryCalls[0]);
        Assert.Contains("rolcreaterole", session.QueryCalls[0]);
        Assert.Contains("rolcanlogin", session.QueryCalls[0]);
        Assert.Contains("ORDER BY rolname", session.QueryCalls[0]);
    }

    [Fact]
    public async Task ListRolesAsync_disposes_session_after_query()
    {
        var session = new RecordingSession(roles: new[] { new RoleSummary("x", false, false, false, false) });
        var sut = new PostgresMetadataService(new FakeFactory(session));

        await sut.ListRolesAsync(SampleProfile(), "pwd");

        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task ListSchemasAsync_queries_information_schema_with_filters_and_ordering()
    {
        var session = new RecordingSession(schemas: new[] { "public", "app", "audit" });
        var sut = new PostgresMetadataService(new FakeFactory(session));

        var result = await sut.ListSchemasAsync(SampleProfile(), "pwd", "mydb");

        Assert.Equal(new[] { "public", "app", "audit" }, result.Select(s => s.Name));
        Assert.Single(session.QueryCalls);
        Assert.Contains("information_schema.schemata", session.QueryCalls[0]);
        Assert.Contains("pg_catalog", session.QueryCalls[0]);
        Assert.Contains("information_schema", session.QueryCalls[0]);
        Assert.Contains("pg_toast", session.QueryCalls[0]);
        Assert.Contains("ORDER BY schema_name", session.QueryCalls[0]);
    }

    [Fact]
    public async Task ListSchemasAsync_opens_session_against_target_database()
    {
        var factory = new RecordingFactory();
        var sut = new PostgresMetadataService(factory);

        await sut.ListSchemasAsync(SampleProfile(), "pwd", "different_db");

        Assert.Equal("different_db", factory.LastProfile?.Database);
    }

    [Fact]
    public async Task ListSchemasAsync_throws_on_blank_database_name()
    {
        var sut = new PostgresMetadataService(new FakeFactory(new RecordingSession()));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.ListSchemasAsync(SampleProfile(), "pwd", "  "));
    }

    [Fact]
    public async Task ListSchemasAsync_throws_on_null_arguments()
    {
        var sut = new PostgresMetadataService(new FakeFactory(new RecordingSession()));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            sut.ListSchemasAsync(null!, "pwd", "db"));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            sut.ListSchemasAsync(SampleProfile(), null!, "db"));
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
    public async Task ListRolesAsync_propagates_exceptions_from_factory()
    {
        var factory = new ThrowingFactory(new InvalidOperationException("boom"));
        var sut = new PostgresMetadataService(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ListRolesAsync(SampleProfile(), "pwd"));
    }

    [Fact]
    public void Constructor_throws_on_null_factory()
    {
        Assert.Throws<ArgumentNullException>(() => new PostgresMetadataService(null!));
    }

    [Fact]
    public async Task ListDatabasesAsync_throws_on_null_arguments()
    {
        var sut = new PostgresMetadataService(new FakeFactory(new RecordingSession()));

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.ListDatabasesAsync(null!, "x"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.ListDatabasesAsync(SampleProfile(), null!));
    }

    [Fact]
    public async Task ListRolesAsync_throws_on_null_arguments()
    {
        var sut = new PostgresMetadataService(new FakeFactory(new RecordingSession()));

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.ListRolesAsync(null!, "x"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.ListRolesAsync(SampleProfile(), null!));
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

    private sealed class RecordingFactory : IPostgresSessionFactory
    {
        private readonly RecordingSession _session = new();
        public ConnectionProfile? LastProfile { get; private set; }

        public Task<IPostgresSession> OpenAsync(
            ConnectionProfile profile,
            string password,
            CancellationToken cancellationToken = default)
        {
            LastProfile = profile;
            return Task.FromResult<IPostgresSession>(_session);
        }
    }

    private sealed class RecordingSession : IPostgresSession
    {
        private readonly IReadOnlyList<string> _databases;
        private readonly IReadOnlyList<RoleSummary> _roles;
        private readonly IReadOnlyList<string> _schemas;

        public RecordingSession(
            IReadOnlyList<string>? databases = null,
            IReadOnlyList<RoleSummary>? roles = null,
            IReadOnlyList<string>? schemas = null)
        {
            _databases = databases ?? Array.Empty<string>();
            _roles = roles ?? Array.Empty<RoleSummary>();
            _schemas = schemas ?? Array.Empty<string>();
        }

        public List<string> QueryCalls { get; } = new();
        public bool Disposed { get; private set; }

        public Task<IReadOnlyList<TRow>> QueryAsync<TRow>(
            string sql,
            Func<DbDataReader, TRow> map,
            CancellationToken cancellationToken = default)
        {
            QueryCalls.Add(sql);
            var rows = new List<TRow>();
            if (sql.Contains("pg_database", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var name in _databases)
                    rows.Add(map(new SingleStringReader(name)));
            }
            else if (sql.Contains("pg_roles", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var role in _roles)
                    rows.Add(map(new RoleRowReader(role)));
            }
            else if (sql.Contains("information_schema.schemata", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var name in _schemas)
                    rows.Add(map(new SingleStringReader(name)));
            }
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

    private abstract class StubReader : DbDataReader
    {
        public override object this[int ordinal] => throw new NotImplementedException();
        public override object this[string name] => throw new NotImplementedException();
        public override int Depth => 0;
        public override bool HasRows => true;
        public override bool IsClosed => false;
        public override int RecordsAffected => -1;
        public override byte GetByte(int ordinal) => throw new NotImplementedException();
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotImplementedException();
        public override char GetChar(int ordinal) => throw new NotImplementedException();
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotImplementedException();
        public override DateTime GetDateTime(int ordinal) => throw new NotImplementedException();
        public override decimal GetDecimal(int ordinal) => throw new NotImplementedException();
        public override double GetDouble(int ordinal) => throw new NotImplementedException();
        public override float GetFloat(int ordinal) => throw new NotImplementedException();
        public override Guid GetGuid(int ordinal) => throw new NotImplementedException();
        public override short GetInt16(int ordinal) => throw new NotImplementedException();
        public override int GetInt32(int ordinal) => throw new NotImplementedException();
        public override long GetInt64(int ordinal) => throw new NotImplementedException();
        public override int GetValues(object[] values) => throw new NotImplementedException();
        public override bool IsDBNull(int ordinal) => false;
        public override bool NextResult() => false;
        public override bool Read() => false;
        public override System.Collections.IEnumerator GetEnumerator() => throw new NotImplementedException();
    }

    private sealed class SingleStringReader : StubReader
    {
        private readonly string _value;
        public SingleStringReader(string value) => _value = value;

        public override int FieldCount => 1;
        public override string GetString(int ordinal) => _value;
        public override bool GetBoolean(int ordinal) => throw new NotImplementedException();
        public override string GetDataTypeName(int ordinal) => "text";
        public override Type GetFieldType(int ordinal) => typeof(string);
        public override string GetName(int ordinal) => "datname";
        public override int GetOrdinal(string name) => 0;
        public override object GetValue(int ordinal) => _value;
    }

    private sealed class RoleRowReader : StubReader
    {
        private readonly RoleSummary _role;
        public RoleRowReader(RoleSummary role) => _role = role;

        public override int FieldCount => 5;

        public override string GetString(int ordinal) => ordinal switch
        {
            0 => _role.Name,
            _ => throw new IndexOutOfRangeException(),
        };

        public override bool GetBoolean(int ordinal) => ordinal switch
        {
            1 => _role.IsSuperuser,
            2 => _role.CanCreateDb,
            3 => _role.CanCreateRole,
            4 => _role.CanLogin,
            _ => throw new IndexOutOfRangeException(),
        };

        public override string GetDataTypeName(int ordinal) =>
            ordinal == 0 ? "text" : "boolean";

        public override Type GetFieldType(int ordinal) =>
            ordinal == 0 ? typeof(string) : typeof(bool);

        public override string GetName(int ordinal) => ordinal switch
        {
            0 => "rolname",
            1 => "rolsuper",
            2 => "rolcreatedb",
            3 => "rolcreaterole",
            4 => "rolcanlogin",
            _ => throw new IndexOutOfRangeException(),
        };

        public override int GetOrdinal(string name) => name switch
        {
            "rolname" => 0,
            "rolsuper" => 1,
            "rolcreatedb" => 2,
            "rolcreaterole" => 3,
            "rolcanlogin" => 4,
            _ => throw new IndexOutOfRangeException(),
        };

        public override object GetValue(int ordinal) => ordinal switch
        {
            0 => _role.Name,
            1 => _role.IsSuperuser,
            2 => _role.CanCreateDb,
            3 => _role.CanCreateRole,
            4 => _role.CanLogin,
            _ => throw new IndexOutOfRangeException(),
        };
    }
}
