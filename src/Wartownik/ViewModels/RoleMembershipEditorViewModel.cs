using System.Collections.ObjectModel;
using Wartownik.Connections;
using Wartownik.Localization;

namespace Wartownik.ViewModels;

/// <summary>
/// One candidate group in the membership dialog: a role the edited member could belong to,
/// with a checkbox reflecting whether it currently does.
/// </summary>
public sealed class MembershipOptionViewModel : ViewModelBase
{
    private bool _isMember;

    public MembershipOptionViewModel(RoleSummary group, bool isMember)
    {
        ArgumentNullException.ThrowIfNull(group);
        Group = group;
        WasMember = isMember;
        _isMember = isMember;
    }

    public RoleSummary Group { get; }

    /// <summary>Membership as it stood when the dialog opened — the baseline we diff against.</summary>
    public bool WasMember { get; }

    public string GroupName => Group.Name;

    public bool IsMember
    {
        get => _isMember;
        set => SetField(ref _isMember, value);
    }

    public bool IsChanged => _isMember != WasMember;
}

/// <summary>
/// Backs the "member of" dialog: pick which group roles a role inherits privileges from.
/// Produces a diff (join these, leave those) rather than a full desired-state list, so applying
/// it never touches memberships the user didn't tick.
/// </summary>
public sealed class RoleMembershipEditorViewModel : ViewModelBase
{
    private string _memberRole = "";

    public ILocalizationService Localization { get; }

    public ObservableCollection<MembershipOptionViewModel> Options { get; } = new();

    public RoleMembershipEditorViewModel(ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);
        Localization = localization;
        Localization.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "Item[]")
            {
                RaisePropertyChanged(nameof(Title));
                RaisePropertyChanged(nameof(EmptyHint));
            }
        };
    }

    public string MemberRole
    {
        get => _memberRole;
        private set
        {
            if (SetField(ref _memberRole, value))
                RaisePropertyChanged(nameof(Title));
        }
    }

    public string Title => string.Format(Localization["Membership.Title"], MemberRole);

    public string EmptyHint => Localization["Membership.NoGroups"];

    public bool HasOptions => Options.Count > 0;

    /// <summary>
    /// Populate the dialog for <paramref name="member"/>. Candidate groups are every other role in
    /// the cluster — PostgreSQL lets any role be a group, so we don't second-guess which ones are
    /// "really" groups; we only exclude the member itself (a role can't contain itself).
    /// </summary>
    public void LoadFor(
        RoleSummary member,
        IReadOnlyList<RoleSummary> allRoles,
        IReadOnlyCollection<string> currentGroups)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(allRoles);
        ArgumentNullException.ThrowIfNull(currentGroups);

        MemberRole = member.Name;
        Options.Clear();

        var groups = new HashSet<string>(currentGroups, StringComparer.Ordinal);
        foreach (var role in allRoles)
        {
            if (string.Equals(role.Name, member.Name, StringComparison.Ordinal))
                continue;
            Options.Add(new MembershipOptionViewModel(role, groups.Contains(role.Name)));
        }

        RaisePropertyChanged(nameof(HasOptions));
    }

    /// <summary>
    /// The ticked/unticked deltas. Empty when the user changed nothing, which the caller treats
    /// as "nothing to do" rather than an error.
    /// </summary>
    public IReadOnlyList<RoleMembershipChange> BuildChanges() =>
        Options
            .Where(o => o.IsChanged)
            .Select(o => new RoleMembershipChange(
                o.GroupName,
                o.IsMember ? GrantOperation.Grant : GrantOperation.Revoke))
            .ToList();
}
