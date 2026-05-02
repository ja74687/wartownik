using Wartownik.Connections;
using Wartownik.Localization;

namespace Wartownik.ViewModels;

public sealed class RoleItemViewModel : ViewModelBase
{
    private readonly ILocalizationService _localization;

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
                RaisePropertyChanged(nameof(Tags));
        };
    }

    public string Name => Summary.Name;

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
