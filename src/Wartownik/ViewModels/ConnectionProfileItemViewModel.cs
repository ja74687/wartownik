using Wartownik.Connections;

namespace Wartownik.ViewModels;

public sealed class ConnectionProfileItemViewModel
{
    public ConnectionProfile Profile { get; }

    public ConnectionProfileItemViewModel(ConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Profile = profile;
    }

    public Guid Id => Profile.Id;
    public string DisplayName => Profile.DisplayName;
    public string Endpoint => $"{Profile.Host}:{Profile.Port} / {Profile.Database} / {Profile.Username}";
}
