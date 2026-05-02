using System.Data.Common;
using Wartownik.Connections;
using Wartownik.Postgres;
using Wartownik.Sql;

namespace Wartownik.UnitTests.Connections;

public class PostgresRoleAdminServiceTests
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
    public void BuildCreateRoleSql_quotes_identifier_and_emits_negative_flags()
    {
        var sql = PostgresRoleAdminService.BuildCreateRoleSql(
            new CreateRoleRequest(
                RoleName: "viewer",
                IsSuperuser: false,
                CanCreateDb: false,
                CanCreateRole: false,
                CanLogin: false,
                RolePassword: null));

        Assert.Equal(
            "CREATE ROLE \"viewer\" NOSUPERUSER NOCREATEDB NOCREATEROLE NOLOGIN",
            sql);
    }

    [Fact]
    public void BuildCreateRoleSql_emits_positive_flags()
    {
        var sql = PostgresRoleAdminService.BuildCreateRoleSql(
            new CreateRoleRequest(
                RoleName: "admin",
                IsSuperuser: true,
                CanCreateDb: true,
                CanCreateRole: true,
                CanLogin: true,
                RolePassword: "secret"));

        Assert.Equal(
            "CREATE ROLE \"admin\" SUPERUSER CREATEDB CREATEROLE LOGIN PASSWORD 'secret'",
            sql);
    }

    [Fact]
    public void BuildCreateRoleSql_skips_password_when_login_is_false()
    {
        var sql = PostgresRoleAdminService.BuildCreateRoleSql(
            new CreateRoleRequest(
                RoleName: "group_a",
                IsSuperuser: false,
                CanCreateDb: false,
                CanCreateRole: false,
                CanLogin: false,
                RolePassword: "ignored"));

        Assert.DoesNotContain("PASSWORD", sql);
    }

    [Fact]
    public void BuildCreateRoleSql_escapes_double_quotes_in_role_name()
    {
        var sql = PostgresRoleAdminService.BuildCreateRoleSql(
            new CreateRoleRequest(
                RoleName: "evil\"name",
                IsSuperuser: false,
                CanCreateDb: false,
                CanCreateRole: false,
                CanLogin: false,
                RolePassword: null));

        Assert.Contains("\"evil\"\"name\"", sql);
    }

    [Fact]
    public void BuildCreateRoleSql_escapes_apostrophes_in_password()
    {
        var sql = PostgresRoleAdminService.BuildCreateRoleSql(
            new CreateRoleRequest(
                RoleName: "u",
                IsSuperuser: false,
                CanCreateDb: false,
                CanCreateRole: false,
                CanLogin: true,
                RolePassword: "p'wd"));

        Assert.Contains("PASSWORD 'p''wd'", sql);
    }

    [Theory]
    [InlineData("alice", "p'wd")]
    [InlineData("bob_admin", "with;semicolon")]
    [InlineData("user.with.dots", "with-- not-a-comment")]
    public void BuildCreateRoleSql_output_passes_PostgresSqlStatementValidator(string roleName, string password)
    {
        var validator = new PostgresSqlStatementValidator();
        var sql = PostgresRoleAdminService.BuildCreateRoleSql(
            new CreateRoleRequest(
                RoleName: roleName,
                IsSuperuser: true,
                CanCreateDb: true,
                CanCreateRole: false,
                CanLogin: true,
                RolePassword: password));

        var result = validator.Validate(sql);
        Assert.True(result.IsAllowed, result.RejectionReason);
        Assert.Equal(SqlStatementCategory.RoleManagement, result.Category);
    }

    [Fact]
    public void BuildAlterRoleSql_emits_attributes_without_password_when_null()
    {
        var sql = PostgresRoleAdminService.BuildAlterRoleSql(
            new AlterRoleRequest(
                RoleName: "alice",
                IsSuperuser: true,
                CanCreateDb: false,
                CanCreateRole: false,
                CanLogin: true,
                NewPassword: null));

        Assert.Equal(
            "ALTER ROLE \"alice\" SUPERUSER NOCREATEDB NOCREATEROLE LOGIN",
            sql);
    }

    [Fact]
    public void BuildAlterRoleSql_includes_password_when_provided()
    {
        var sql = PostgresRoleAdminService.BuildAlterRoleSql(
            new AlterRoleRequest("alice", false, false, false, true, "newpw"));

        Assert.Contains("PASSWORD 'newpw'", sql);
    }

    [Fact]
    public void BuildAlterRoleSql_escapes_quotes_in_identifier_and_password()
    {
        var sql = PostgresRoleAdminService.BuildAlterRoleSql(
            new AlterRoleRequest("a\"b", false, false, false, true, "p'wd"));

        Assert.Contains("\"a\"\"b\"", sql);
        Assert.Contains("'p''wd'", sql);
    }

    [Theory]
    [InlineData("alice")]
    [InlineData("user_name")]
    public void BuildAlterRoleSql_output_passes_validator(string roleName)
    {
        var validator = new PostgresSqlStatementValidator();
        var sql = PostgresRoleAdminService.BuildAlterRoleSql(
            new AlterRoleRequest(roleName, true, true, true, true, "pw"));

        var result = validator.Validate(sql);
        Assert.True(result.IsAllowed, result.RejectionReason);
        Assert.Equal(SqlStatementCategory.RoleManagement, result.Category);
    }

    [Fact]
    public async Task AlterRoleAsync_executes_built_sql_via_session()
    {
        var session = new RecordingSession();
        var sut = new PostgresRoleAdminService(new FakeFactory(session));

        await sut.AlterRoleAsync(SampleProfile(), "pwd",
            new AlterRoleRequest("alice", true, false, false, false, null));

        Assert.Single(session.ExecuteCalls);
        Assert.StartsWith("ALTER ROLE \"alice\"", session.ExecuteCalls[0]);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task AlterRoleAsync_throws_on_blank_role_name()
    {
        var sut = new PostgresRoleAdminService(new FakeFactory(new RecordingSession()));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.AlterRoleAsync(SampleProfile(), "pwd",
                new AlterRoleRequest("  ", false, false, false, false, null)));
    }

    [Fact]
    public async Task AlterRoleAsync_throws_on_null_arguments()
    {
        var sut = new PostgresRoleAdminService(new FakeFactory(new RecordingSession()));
        var request = new AlterRoleRequest("alice", false, false, false, false, null);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            sut.AlterRoleAsync(null!, "pwd", request));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            sut.AlterRoleAsync(SampleProfile(), null!, request));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            sut.AlterRoleAsync(SampleProfile(), "pwd", null!));
    }

    [Fact]
    public void BuildDropRoleSql_quotes_identifier()
    {
        var sql = PostgresRoleAdminService.BuildDropRoleSql("alice");

        Assert.Equal("DROP ROLE \"alice\"", sql);
    }

    [Fact]
    public void BuildDropRoleSql_escapes_double_quotes()
    {
        var sql = PostgresRoleAdminService.BuildDropRoleSql("evil\"name");

        Assert.Equal("DROP ROLE \"evil\"\"name\"", sql);
    }

    [Theory]
    [InlineData("alice")]
    [InlineData("user_with_underscore")]
    [InlineData("user.with.dots")]
    public void BuildDropRoleSql_output_passes_validator(string roleName)
    {
        var validator = new PostgresSqlStatementValidator();
        var sql = PostgresRoleAdminService.BuildDropRoleSql(roleName);

        var result = validator.Validate(sql);
        Assert.True(result.IsAllowed, result.RejectionReason);
        Assert.Equal(SqlStatementCategory.RoleManagement, result.Category);
    }

    [Fact]
    public async Task DropRoleAsync_executes_built_sql_via_session()
    {
        var session = new RecordingSession();
        var sut = new PostgresRoleAdminService(new FakeFactory(session));

        await sut.DropRoleAsync(SampleProfile(), "pwd", "alice");

        Assert.Single(session.ExecuteCalls);
        Assert.Equal("DROP ROLE \"alice\"", session.ExecuteCalls[0]);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task DropRoleAsync_throws_on_blank_role_name()
    {
        var sut = new PostgresRoleAdminService(new FakeFactory(new RecordingSession()));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.DropRoleAsync(SampleProfile(), "pwd", "  "));
    }

    [Fact]
    public async Task DropRoleAsync_throws_on_null_arguments()
    {
        var sut = new PostgresRoleAdminService(new FakeFactory(new RecordingSession()));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            sut.DropRoleAsync(null!, "pwd", "alice"));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            sut.DropRoleAsync(SampleProfile(), null!, "alice"));
    }

    [Fact]
    public async Task CreateRoleAsync_executes_built_sql_via_session()
    {
        var session = new RecordingSession();
        var sut = new PostgresRoleAdminService(new FakeFactory(session));

        await sut.CreateRoleAsync(
            SampleProfile(),
            "profile-pwd",
            new CreateRoleRequest("alice", false, false, false, true, "pw"));

        Assert.Single(session.ExecuteCalls);
        Assert.StartsWith("CREATE ROLE \"alice\"", session.ExecuteCalls[0]);
        Assert.Contains("LOGIN PASSWORD 'pw'", session.ExecuteCalls[0]);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task CreateRoleAsync_throws_on_blank_role_name()
    {
        var sut = new PostgresRoleAdminService(new FakeFactory(new RecordingSession()));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.CreateRoleAsync(SampleProfile(), "pwd",
                new CreateRoleRequest("   ", false, false, false, false, null)));
    }

    [Fact]
    public async Task CreateRoleAsync_throws_on_null_arguments()
    {
        var sut = new PostgresRoleAdminService(new FakeFactory(new RecordingSession()));
        var request = new CreateRoleRequest("a", false, false, false, false, null);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            sut.CreateRoleAsync(null!, "pwd", request));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            sut.CreateRoleAsync(SampleProfile(), null!, request));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            sut.CreateRoleAsync(SampleProfile(), "pwd", null!));
    }

    [Fact]
    public void Constructor_throws_on_null_factory()
    {
        Assert.Throws<ArgumentNullException>(() => new PostgresRoleAdminService(null!));
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

    private sealed class RecordingSession : IPostgresSession
    {
        public List<string> ExecuteCalls { get; } = new();
        public bool Disposed { get; private set; }

        public Task<IReadOnlyList<TRow>> QueryAsync<TRow>(
            string sql,
            Func<DbDataReader, TRow> map,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TRow>>(Array.Empty<TRow>());

        public Task ExecuteAsync(string sql, CancellationToken cancellationToken = default)
        {
            ExecuteCalls.Add(sql);
            return Task.CompletedTask;
        }

        public Task ExecuteInTransactionAsync(IReadOnlyList<string> statements, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
