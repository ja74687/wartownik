using System.Collections.ObjectModel;
using Wartownik.Connections;
using Wartownik.Localization;

namespace Wartownik.ViewModels;

public sealed class ProfileDetailsViewModel : ViewModelBase
{
    private readonly IConnectionProfileService _profiles;
    private readonly IPostgresMetadataService _metadata;

    private bool _isLoading;
    private string? _errorMessage;

    public ConnectionProfile Profile { get; }
    public ILocalizationService Localization { get; }
    public ObservableCollection<DatabaseItemViewModel> Databases { get; } = new();

    public ProfileDetailsViewModel(
        ConnectionProfile profile,
        ILocalizationService localization,
        IConnectionProfileService profiles,
        IPostgresMetadataService metadata)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(metadata);

        Profile = profile;
        Localization = localization;
        _profiles = profiles;
        _metadata = metadata;
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetField(ref _isLoading, value))
                RaisePropertyChanged(nameof(IsContentVisible));
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value))
                RaisePropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_errorMessage);

    public bool HasDatabases => Databases.Count > 0;

    public bool IsEmpty => !IsLoading && !HasError && !HasDatabases;

    public bool IsContentVisible => !IsLoading;

    public string Endpoint => $"{Profile.Host}:{Profile.Port} / {Profile.Database} / {Profile.Username}";

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        ErrorMessage = null;
        Databases.Clear();
        RaisePropertyChanged(nameof(HasDatabases));
        RaisePropertyChanged(nameof(IsEmpty));

        try
        {
            var password = await _profiles
                .GetPasswordAsync(Profile.Id, cancellationToken)
                .ConfigureAwait(true) ?? "";

            var loaded = await _metadata
                .ListDatabasesAsync(Profile, password, cancellationToken)
                .ConfigureAwait(true);

            foreach (var summary in loaded)
                Databases.Add(new DatabaseItemViewModel(summary));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
            RaisePropertyChanged(nameof(HasDatabases));
            RaisePropertyChanged(nameof(IsEmpty));
        }
    }
}
