using Wartownik.Postgres;

namespace Wartownik.Connections;

public sealed class PostgresRoleMembershipService : IPostgresRoleMembershipService
{
    // pg_auth_members holds one row per edge, with OIDs on both ends — join pg_roles twice to
    // turn them back into names. Cluster-wide, so it doesn't matter which database we're on.
    private const string ListMembershipsSql = """
        SELECT m.rolname AS member_role, g.rolname AS group_role
        FROM pg_catalog.pg_auth_members am
        JOIN pg_catalog.pg_roles m ON m.oid = am.member
        JOIN pg_catalog.pg_roles g ON g.oid = am.roleid
        ORDER BY m.rolname, g.rolname
        """;

    private readonly IPostgresSessionFactory _sessionFactory;

    public PostgresRoleMembershipService(IPostgresSessionFactory sessionFactory)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        _sessionFactory = sessionFactory;
    }

    public async Task<IReadOnlyList<RoleMembership>> ListMembershipsAsync(
        ConnectionProfile profile,
        string profilePassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(profilePassword);

        await using var session = await _sessionFactory
            .OpenAsync(profile, profilePassword, cancellationToken)
            .ConfigureAwait(false);

        return await session.QueryAsync(
            ListMembershipsSql,
            reader => new RoleMembership(
                MemberRole: reader.GetString(0),
                GroupRole: reader.GetString(1)),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task ApplyMembershipChangesAsync(
        ConnectionProfile profile,
        string profilePassword,
        string memberRole,
        IReadOnlyList<RoleMembershipChange> changes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(profilePassword);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberRole);
        ArgumentNullException.ThrowIfNull(changes);

        if (changes.Count == 0)
            return;

        var statements = BuildStatements(memberRole, changes);

        await using var session = await _sessionFactory
            .OpenAsync(profile, profilePassword, cancellationToken)
            .ConfigureAwait(false);

        await session.ExecuteInTransactionAsync(statements, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Turns a member's pending changes into GRANT/REVOKE statements. Internal so the unit tests
    /// (and any future SQL preview) can assert on the exact SQL without touching a database.
    /// </summary>
    internal static IReadOnlyList<string> BuildStatements(
        string memberRole,
        IReadOnlyList<RoleMembershipChange> changes)
    {
        var member = QuoteIdentifier(memberRole);
        var statements = new List<string>(changes.Count);

        foreach (var change in changes)
        {
            var group = QuoteIdentifier(change.GroupRole);
            statements.Add(change.Operation == GrantOperation.Grant
                ? $"GRANT {group} TO {member}"
                : $"REVOKE {group} FROM {member}");
        }

        return statements;
    }

    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
