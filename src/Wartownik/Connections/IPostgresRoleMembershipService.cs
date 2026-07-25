namespace Wartownik.Connections;

/// <summary>
/// Reads and applies role membership — "alice is a member of devs", i.e. GRANT devs TO alice.
///
/// Membership is how permissions are meant to be organised in PostgreSQL: privileges go to a
/// group role, and login roles inherit them by being members. This service covers only the
/// membership edges between roles; the privileges themselves stay with
/// <see cref="IPostgresGrantService"/>.
///
/// Out of scope here: WITH ADMIN OPTION, and the grantor recorded on each edge.
/// </summary>
public interface IPostgresRoleMembershipService
{
    /// <summary>
    /// Every membership edge in the cluster. Callers index it themselves rather than querying
    /// per role, because the roles screen needs all of them at once anyway.
    /// </summary>
    Task<IReadOnlyList<RoleMembership>> ListMembershipsAsync(
        ConnectionProfile profile,
        string profilePassword,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds and removes groups for a single member role, as one transactional batch.
    /// </summary>
    Task ApplyMembershipChangesAsync(
        ConnectionProfile profile,
        string profilePassword,
        string memberRole,
        IReadOnlyList<RoleMembershipChange> changes,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One membership edge: <paramref name="MemberRole"/> is a member of <paramref name="GroupRole"/>
/// and therefore inherits its privileges.
/// </summary>
public sealed record RoleMembership(string MemberRole, string GroupRole);

/// <summary>
/// One pending membership flip for a role — joining or leaving a single group.
/// </summary>
public sealed record RoleMembershipChange(string GroupRole, GrantOperation Operation);
