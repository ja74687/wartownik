using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Wartownik.Connections;
using Wartownik.Dialogs;
using Wartownik.Localization;

namespace Wartownik.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    public delegate ProfileDetailsViewModel ProfileDetailsFactory(ConnectionProfile profile);

    private readonly IConnectionProfileService _profiles;
    private readonly IConnectionProfileEditor _editor;
    private readonly IConfirmationDialog _confirmation;
    private readonly IConnectionTester _tester;
    private readonly IPostgresMetadataService _metadata;
    private readonly ProfileDetailsFactory _detailsFactory;

    private readonly List<ConnectionProfileItemViewModel> _allProfiles = new();
    private ProfileDetailsViewModel? _details;
    private string _searchFilter = "";

    public ILocalizationService Localization { get; }

    public ObservableCollection<ConnectionProfileItemViewModel> Profiles { get; } = new();

    public AsyncRelayCommand AddProfileCommand { get; }
    public AsyncRelayCommand EditProfileCommand { get; }
    public AsyncRelayCommand DeleteProfileCommand { get; }
    public AsyncRelayCommand OpenProfileCommand { get; }
    public AsyncRelayCommand BackToProfilesCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }

    public MainWindowViewModel(
        ILocalizationService localization,
        IConnectionProfileService profiles,
        IConnectionProfileEditor editor,
        IConfirmationDialog confirmation,
        IConnectionTester tester,
        IPostgresMetadataService metadata,
        ProfileDetailsFactory detailsFactory)
    {
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(confirmation);
        ArgumentNullException.ThrowIfNull(tester);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(detailsFactory);

        Localization = localization;
        _profiles = profiles;
        _editor = editor;
        _confirmation = confirmation;
        _tester = tester;
        _metadata = metadata;
        _detailsFactory = detailsFactory;

        AddProfileCommand = new AsyncRelayCommand(AddProfileAsync);
        EditProfileCommand = new AsyncRelayCommand(parameter => EditProfileAsync(parameter));
        DeleteProfileCommand = new AsyncRelayCommand(parameter => DeleteProfileAsync(parameter));
        OpenProfileCommand = new AsyncRelayCommand(parameter => OpenProfileAsync(parameter));
        BackToProfilesCommand = new AsyncRelayCommand(BackToProfilesAsync);
        RefreshCommand = new AsyncRelayCommand(LoadProfilesAsync);

        Localization.PropertyChanged += OnLocalizationChanged;
    }

    public ProfileDetailsViewModel? Details
    {
        get => _details;
        private set
        {
            if (_details is not null)
                _details.PropertyChanged -= OnDetailsPropertyChanged;
            if (SetField(ref _details, value))
            {
                RaisePropertyChanged(nameof(IsViewingDetails));
                RaisePropertyChanged(nameof(IsAtProfileLevel));
                RaisePropertyChanged(nameof(IsAtDatabaseLevel));
                if (_details is not null)
                    _details.PropertyChanged += OnDetailsPropertyChanged;
            }
        }
    }

    public bool IsViewingDetails => _details is not null;

    public bool IsAtProfileLevel =>
        _details is not null && !_details.IsViewingDatabase;

    public bool IsAtDatabaseLevel =>
        _details is not null && _details.IsViewingDatabase;

    private void OnDetailsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProfileDetailsViewModel.IsViewingDatabase))
        {
            RaisePropertyChanged(nameof(IsAtProfileLevel));
            RaisePropertyChanged(nameof(IsAtDatabaseLevel));
        }
    }

    public IReadOnlyList<CultureInfo> AvailableLanguages => Localization.AvailableLanguages;

    public CultureInfo SelectedLanguage
    {
        get => Localization.CurrentLanguage;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (string.Equals(Localization.CurrentLanguage.Name, value.Name, StringComparison.OrdinalIgnoreCase))
                return;
            Localization.SetLanguage(value);
        }
    }

    public bool HasProfiles => Profiles.Count > 0;

    public int TotalProfileCount => _allProfiles.Count;

    public string ProfilesCountLabel =>
        _allProfiles.Count switch
        {
            0 => Localization["Profiles.CountConfiguredZero"],
            1 => Localization["Profiles.CountConfiguredOne"],
            _ => string.Format(Localization["Profiles.CountConfigured"], _allProfiles.Count),
        };

    public string SearchFilter
    {
        get => _searchFilter;
        set
        {
            if (SetField(ref _searchFilter, value ?? ""))
                ApplyFilter();
        }
    }

    public async Task LoadProfilesAsync()
    {
        var loaded = await _profiles.ListAsync().ConfigureAwait(true);
        _allProfiles.Clear();
        foreach (var profile in loaded)
            _allProfiles.Add(new ConnectionProfileItemViewModel(profile));
        ApplyFilter();
        RaisePropertyChanged(nameof(TotalProfileCount));
        RaisePropertyChanged(nameof(ProfilesCountLabel));

        // Background refresh of status + counters per profile (fire-and-forget).
        foreach (var item in _allProfiles)
            _ = RefreshProfileMetaAsync(item);
    }

    private void ApplyFilter()
    {
        Profiles.Clear();
        var query = _searchFilter.Trim();
        foreach (var item in _allProfiles)
        {
            if (query.Length == 0 ||
                item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Endpoint.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                Profiles.Add(item);
            }
        }
        RaisePropertyChanged(nameof(HasProfiles));
    }

    private async Task RefreshProfileMetaAsync(ConnectionProfileItemViewModel item)
    {
        item.Status = ConnectionStatus.Checking;
        try
        {
            var password = await _profiles.GetPasswordAsync(item.Id).ConfigureAwait(true) ?? "";
            var test = await _tester.TestAsync(item.Profile, password).ConfigureAwait(true);

            if (!test.Success)
            {
                item.Status = ConnectionStatus.Disconnected;
                return;
            }

            item.Status = ConnectionStatus.Connected;

            try
            {
                var dbs = await _metadata.ListDatabasesAsync(item.Profile, password).ConfigureAwait(true);
                var roles = await _metadata.ListRolesAsync(item.Profile, password).ConfigureAwait(true);
                item.DatabaseCount = dbs.Count;
                item.UserCount = roles.Count(r => r.CanLogin);
            }
            catch
            {
                // Connection works but metadata read failed (permissions?). Leave counters empty.
            }
        }
        catch
        {
            item.Status = ConnectionStatus.Disconnected;
        }
    }

    private async Task AddProfileAsync()
    {
        var result = await _editor.AddAsync().ConfigureAwait(true);
        if (result is null)
            return;

        await _profiles.SaveAsync(result.Profile, result.Password).ConfigureAwait(true);
        await LoadProfilesAsync().ConfigureAwait(true);
    }

    private async Task OpenProfileAsync(object? parameter)
    {
        if (parameter is not ConnectionProfileItemViewModel item)
            return;

        var details = _detailsFactory(item.Profile);
        Details = details;
        await details.LoadAsync().ConfigureAwait(true);
    }

    private Task BackToProfilesAsync()
    {
        Details = null;
        return Task.CompletedTask;
    }

    private async Task EditProfileAsync(object? parameter)
    {
        if (parameter is not ConnectionProfileItemViewModel item)
            return;

        var password = await _profiles.GetPasswordAsync(item.Id).ConfigureAwait(true) ?? "";
        var result = await _editor.EditAsync(item.Profile, password).ConfigureAwait(true);
        if (result is null)
            return;

        await _profiles.SaveAsync(result.Profile, result.Password).ConfigureAwait(true);
        await LoadProfilesAsync().ConfigureAwait(true);
    }

    private async Task DeleteProfileAsync(object? parameter)
    {
        if (parameter is not ConnectionProfileItemViewModel item)
            return;

        var request = new ConfirmationRequest(
            Title: Localization["Profiles.DeleteConfirmTitle"],
            Message: string.Format(Localization["Profiles.DeleteConfirmMessage"], item.DisplayName),
            ConfirmLabel: Localization["Profiles.Delete"],
            CancelLabel: Localization["Common.Cancel"],
            IsDestructive: true);

        if (!await _confirmation.ConfirmAsync(request).ConfigureAwait(true))
            return;

        await _profiles.DeleteAsync(item.Id).ConfigureAwait(true);
        await LoadProfilesAsync().ConfigureAwait(true);
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ILocalizationService.CurrentLanguage))
            RaisePropertyChanged(nameof(SelectedLanguage));
        if (e.PropertyName == "Item[]")
            RaisePropertyChanged(nameof(ProfilesCountLabel));
    }
}
