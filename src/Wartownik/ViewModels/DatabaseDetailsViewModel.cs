using System.Collections.ObjectModel;
using Wartownik.Connections;
using Wartownik.Localization;

namespace Wartownik.ViewModels;

public sealed class DatabaseDetailsViewModel : ViewModelBase
{
    private readonly IConnectionProfileService _profiles;
    private readonly IPostgresMetadataService _metadata;

    private bool _isLoading;
    private string? _errorMessage;

    public ConnectionProfile Profile { get; }
    public string DatabaseName { get; }
    public ILocalizationService Localization { get; }
    public ObservableCollection<SchemaItemViewModel> Schemas { get; } = new();

    public DatabaseDetailsViewModel(
        ConnectionProfile profile,
        string databaseName,
        ILocalizationService localization,
        IConnectionProfileService profiles,
        IPostgresMetadataService metadata)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(metadata);

        Profile = profile;
        DatabaseName = databaseName;
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
            {
                RaisePropertyChanged(nameof(IsContentVisible));
                RaisePropertyChanged(nameof(IsSchemasEmpty));
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value))
            {
                RaisePropertyChanged(nameof(HasError));
                RaisePropertyChanged(nameof(IsContentVisible));
                RaisePropertyChanged(nameof(IsSchemasEmpty));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_errorMessage);

    public bool HasSchemas => Schemas.Count > 0;

    public bool IsSchemasEmpty => !IsLoading && !HasError && !HasSchemas;

    public bool IsContentVisible => !IsLoading && !HasError;

    public string Endpoint => $"{Profile.Host}:{Profile.Port} / {DatabaseName} / {Profile.Username}";

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        ErrorMessage = null;
        Schemas.Clear();
        RaisePropertyChanged(nameof(HasSchemas));
        RaisePropertyChanged(nameof(IsSchemasEmpty));

        try
        {
            var password = await _profiles
                .GetPasswordAsync(Profile.Id, cancellationToken)
                .ConfigureAwait(true) ?? "";

            var loaded = await _metadata
                .ListSchemasAsync(Profile, password, DatabaseName, cancellationToken)
                .ConfigureAwait(true);

            foreach (var summary in loaded)
                Schemas.Add(new SchemaItemViewModel(summary));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
            RaisePropertyChanged(nameof(HasSchemas));
            RaisePropertyChanged(nameof(IsSchemasEmpty));
        }
    }
}
