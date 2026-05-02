using System.Text;
using Wartownik.Postgres;

namespace Wartownik.Connections;

public sealed class PostgresRoleAdminService : IPostgresRoleAdminService
{
    private readonly IPostgresSessionFactory _sessionFactory;

    public PostgresRoleAdminService(IPostgresSessionFactory sessionFactory)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        _sessionFactory = sessionFactory;
    }

    public async Task CreateRoleAsync(
        ConnectionProfile profile,
        string profilePassword,
        CreateRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(profilePassword);
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.RoleName))
            throw new ArgumentException("Role name must not be blank.", nameof(request));

        var sql = BuildCreateRoleSql(request);

        await using var session = await _sessionFactory
            .OpenAsync(profile, profilePassword, cancellationToken)
            .ConfigureAwait(false);

        await session.ExecuteAsync(sql, cancellationToken).ConfigureAwait(false);
    }

    public async Task AlterRoleAsync(
        ConnectionProfile profile,
        string profilePassword,
        AlterRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(profilePassword);
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.RoleName))
            throw new ArgumentException("Role name must not be blank.", nameof(request));

        var sql = BuildAlterRoleSql(request);

        await using var session = await _sessionFactory
            .OpenAsync(profile, profilePassword, cancellationToken)
            .ConfigureAwait(false);

        await session.ExecuteAsync(sql, cancellationToken).ConfigureAwait(false);
    }

    internal static string BuildAlterRoleSql(AlterRoleRequest request)
    {
        var sb = new StringBuilder();
        sb.Append("ALTER ROLE ").Append(QuoteIdentifier(request.RoleName));
        sb.Append(request.IsSuperuser ? " SUPERUSER" : " NOSUPERUSER");
        sb.Append(request.CanCreateDb ? " CREATEDB" : " NOCREATEDB");
        sb.Append(request.CanCreateRole ? " CREATEROLE" : " NOCREATEROLE");
        sb.Append(request.CanLogin ? " LOGIN" : " NOLOGIN");

        if (!string.IsNullOrEmpty(request.NewPassword))
            sb.Append(" PASSWORD ").Append(QuoteLiteral(request.NewPassword));

        return sb.ToString();
    }

    public async Task DropRoleAsync(
        ConnectionProfile profile,
        string profilePassword,
        string roleName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(profilePassword);
        if (string.IsNullOrWhiteSpace(roleName))
            throw new ArgumentException("Role name must not be blank.", nameof(roleName));

        var sql = BuildDropRoleSql(roleName);

        await using var session = await _sessionFactory
            .OpenAsync(profile, profilePassword, cancellationToken)
            .ConfigureAwait(false);

        await session.ExecuteAsync(sql, cancellationToken).ConfigureAwait(false);
    }

    internal static string BuildDropRoleSql(string roleName) =>
        "DROP ROLE " + QuoteIdentifier(roleName);

    internal static string BuildCreateRoleSql(CreateRoleRequest request)
    {
        var sb = new StringBuilder();
        sb.Append("CREATE ROLE ").Append(QuoteIdentifier(request.RoleName));
        sb.Append(request.IsSuperuser ? " SUPERUSER" : " NOSUPERUSER");
        sb.Append(request.CanCreateDb ? " CREATEDB" : " NOCREATEDB");
        sb.Append(request.CanCreateRole ? " CREATEROLE" : " NOCREATEROLE");
        sb.Append(request.CanLogin ? " LOGIN" : " NOLOGIN");

        if (request.CanLogin && !string.IsNullOrEmpty(request.RolePassword))
        {
            sb.Append(" PASSWORD ").Append(QuoteLiteral(request.RolePassword));
        }

        return sb.ToString();
    }

    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static string QuoteLiteral(string value) =>
        "'" + value.Replace("'", "''") + "'";
}
