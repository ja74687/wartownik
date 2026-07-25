namespace Wartownik.Connections;

/// <summary>
/// Shows the "member of" picker for a role. Returns the membership deltas the user confirmed,
/// or null when they cancelled. An empty list means they pressed Save without changing anything.
/// </summary>
public interface IRoleMembershipEditor
{
    Task<IReadOnlyList<RoleMembershipChange>?> EditAsync(
        RoleSummary member,
        IReadOnlyList<RoleSummary> allRoles,
        IReadOnlyCollection<string> currentGroups,
        CancellationToken cancellationToken = default);
}
