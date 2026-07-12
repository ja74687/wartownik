using Wartownik.Connections;

namespace Wartownik.ViewModels;

/// <summary>
/// One individual staged change in the sticky bar's selective-apply list: a single
/// (role, schema, privilege) grant or revoke, with a checkbox controlling whether the
/// next "Apply selected" includes it.
///
/// Selection state is owned by the parent matrix VM (in a de-selected set) so it survives
/// the pending list being rebuilt when the underlying edits change — this VM just reflects
/// the initial state and notifies the parent when the user ticks the box.
/// </summary>
public sealed class PendingChangeViewModel : ViewModelBase
{
    private readonly Action<PendingChangeViewModel> _onSelectionChanged;
    private bool _isSelected;

    public PendingChangeViewModel(
        string roleName,
        string schemaName,
        GrantPrivilege privilege,
        bool isGrant,
        bool isSelected,
        Action<PendingChangeViewModel> onSelectionChanged)
    {
        ArgumentException.ThrowIfNullOrEmpty(roleName);
        ArgumentException.ThrowIfNullOrEmpty(schemaName);
        ArgumentNullException.ThrowIfNull(onSelectionChanged);

        RoleName = roleName;
        SchemaName = schemaName;
        Privilege = privilege;
        IsGrant = isGrant;
        _isSelected = isSelected;
        _onSelectionChanged = onSelectionChanged;
    }

    public string RoleName { get; }
    public string SchemaName { get; }
    public GrantPrivilege Privilege { get; }
    public bool IsGrant { get; }
    public bool IsRevoke => !IsGrant;

    /// <summary>e.g. "public.SELECT" — schema-qualified privilege name.</summary>
    public string Label => $"{SchemaName}.{Privilege.ToString().ToUpperInvariant()}";

    /// <summary>"+" for a grant, "✕" for a revoke — matches the matrix cell palette.</summary>
    public string Glyph => IsGrant ? "+" : "✕";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetField(ref _isSelected, value))
                _onSelectionChanged(this);
        }
    }
}
