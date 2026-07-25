using Wartownik.Connections;
using Wartownik.Localization;

namespace Wartownik.ViewModels;

public sealed class RoleItemViewModel : ViewModelBase
{
    private readonly ILocalizationService _localization;
    private IReadOnlyList<string> _memberOf = Array.Empty<string>();

    public RoleSummary Summary { get; }

    public RoleItemViewModel(RoleSummary summary, ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(localization);
        Summary = summary;
        _localization = localization;
        _localization.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "Item[]")
            {
                RaisePropertyChanged(nameof(Tags));
                RaisePropertyChanged(nameof(MemberOfLabel));
            }
        };
    }

    public string Name => Summary.Name;

    /// <summary>
    /// Group roles this role inherits privileges from. Set after the roles list loads, since
    /// membership comes from a separate catalog query.
    /// </summary>
    public IReadOnlyList<string> MemberOf
    {
        get => _memberOf;
        set
        {
            _memberOf = value ?? Array.Empty<string>();
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(HasMemberships));
            RaisePropertyChanged(nameof(MemberOfLabel));
        }
    }

    public bool HasMemberships => _memberOf.Count > 0;

    public string MemberOfLabel =>
        _memberOf.Count == 0 ? "" : $"{_localization["Membership.MemberOf"]} {string.Join(", ", _memberOf)}";

    public bool IsSuperuser => Summary.IsSuperuser;
    public bool CanLogin => Summary.CanLogin;

    public string Tags
    {
        get
        {
            var parts = new List<string>(2);
            if (Summary.IsSuperuser) parts.Add(_localization["Roles.Superuser"]);
            parts.Add(_localization[Summary.CanLogin ? "Roles.CanLogin" : "Roles.NoLogin"]);
            return string.Join(" · ", parts);
        }
    }
}
