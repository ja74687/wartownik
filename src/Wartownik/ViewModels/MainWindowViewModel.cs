using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Wartownik.Connections;
using Wartownik.Dialogs;
using Wartownik.Localization;
using Wartownik.Settings;
using Wartownik.Updates;

namespace Wartownik.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    public delegate ProfileDetailsViewModel ProfileDetailsFactory(ConnectionProfile profile);

    private readonly IConnectionProfileService _profiles;
    private readonly IConnectionProfileEditor _editor;
    private readonly IConfirmationDialog _confirmation;
    private readonly IConnectionTester _tester;
    private readonly IPostgresMetadataService _metadata;
    private readonly IUpdateService? _updates;
    private readonly IProfileExportDialog? _profileExport;
    private readonly IAppSettingsStore? _settingsStore;
    private readonly ProfileDetailsFactory _detailsFactory;

    private readonly List<ConnectionProfileItemViewModel> _allProfiles = new();
    private ProfileDetailsViewModel? _details;
    private AppSettings _settings = new();
    private bool _isViewingSettings;
    private string _searchFilter = "";
    private UpdateInfo? _availableUpdate;
    private bool _isApplyingUpdate;
    private string? _statusMessage;

    public ILocalizationService Localization { get; }

    public ObservableCollection<ConnectionProfileItemViewModel> Profiles { get; } = new();

    public AsyncRelayCommand AddProfileCommand { get; }
    public AsyncRelayCommand EditProfileCommand { get; }
    public AsyncRelayCommand DeleteProfileCommand { get; }
    public AsyncRelayCommand OpenProfileCommand { get; }
    public AsyncRelayCommand BackToProfilesCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand InstallUpdateCommand { get; }
    public RelayCommand DismissUpdateCommand { get; }
    public AsyncRelayCommand ExportProfileCommand { get; }
    public RelayCommand OpenSettingsCommand { get; }
    public RelayCommand CloseSettingsCommand { get; }

    public MainWindowViewModel(
        ILocalizationService localization,
        IConnectionProfileService profiles,
        IConnectionProfileEditor editor,
        IConfirmationDialog confirmation,
        IConnectionTester tester,
        IPostgresMetadataService metadata,
        ProfileDetailsFactory detailsFactory,
        IUpdateService? updates = null,
        IProfileExportDialog? profileExport = null,
        IAppSettingsStore? settingsStore = null)
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
        _updates = updates;
        _profileExport = profileExport;
        _settingsStore = settingsStore;
        _detailsFactory = detailsFactory;

        AddProfileCommand = new AsyncRelayCommand(AddProfileAsync);
        EditProfileCommand = new AsyncRelayCommand(parameter => EditProfileAsync(parameter));
        DeleteProfileCommand = new AsyncRelayCommand(parameter => DeleteProfileAsync(parameter));
        OpenProfileCommand = new AsyncRelayCommand(parameter => OpenProfileAsync(parameter));
        BackToProfilesCommand = new AsyncRelayCommand(BackToProfilesAsync);
        RefreshCommand = new AsyncRelayCommand(LoadProfilesAsync);
        InstallUpdateCommand = new AsyncRelayCommand(InstallUpdateAsync, () => _availableUpdate is not null && !_isApplyingUpdate);
        DismissUpdateCommand = new RelayCommand(() => AvailableUpdate = null);
        ExportProfileCommand = new AsyncRelayCommand(ExportProfileAsync);
        OpenSettingsCommand = new RelayCommand(() => IsViewingSettings = true);
        CloseSettingsCommand = new RelayCommand(() => IsViewingSettings = false);

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
                RaisePropertyChanged(nameof(IsProfileLevelActive));
                RaisePropertyChanged(nameof(IsDatabaseLevelActive));
                RaisePropertyChanged(nameof(ShowProfileList));
                RaisePropertyChanged(nameof(ShowProfileDetails));
                if (_details is not null)
                    _details.PropertyChanged += OnDetailsPropertyChanged;
            }
        }
    }

    public bool IsViewingDetails => _details is not null;

    /// <summary>
    /// Whether the app-level Settings screen is showing. It overlays whatever the user was on
    /// (profile list or a profile's details) without discarding it, so closing Settings returns
    /// them to where they were.
    /// </summary>
    public bool IsViewingSettings
    {
        get => _isViewingSettings;
        set
        {
            if (SetField(ref _isViewingSettings, value))
            {
                RaisePropertyChanged(nameof(ShowProfileList));
                RaisePropertyChanged(nameof(ShowProfileDetails));
                RaisePropertyChanged(nameof(IsProfileLevelActive));
                RaisePropertyChanged(nameof(IsDatabaseLevelActive));
            }
        }
    }

    // The content region shows exactly one of three views. Settings, when open, wins over both;
    // otherwise it's the profile list or a profile's details depending on whether one is open.
    public bool ShowProfileList => !IsViewingDetails && !_isViewingSettings;
    public bool ShowProfileDetails => IsViewingDetails && !_isViewingSettings;

    public bool IsAtProfileLevel =>
        _details is not null && !_details.IsViewingDatabase;

    public bool IsAtDatabaseLevel =>
        _details is not null && _details.IsViewingDatabase;

    // Which breadcrumb is highlighted. Separate from IsAt*Level (which drives visibility) because
    // the trail stays on screen while Settings overlays it — but Settings is then the active
    // location, so no breadcrumb may claim the highlight at the same time.
    public bool IsProfileLevelActive => IsAtProfileLevel && !_isViewingSettings;
    public bool IsDatabaseLevelActive => IsAtDatabaseLevel && !_isViewingSettings;

    private void OnDetailsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProfileDetailsViewModel.IsViewingDatabase))
        {
            RaisePropertyChanged(nameof(IsAtProfileLevel));
            RaisePropertyChanged(nameof(IsAtDatabaseLevel));
            RaisePropertyChanged(nameof(IsProfileLevelActive));
            RaisePropertyChanged(nameof(IsDatabaseLevelActive));
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
            _settings = _settings with { Language = value.Name };
            _ = PersistSettingsAsync();
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

    /// <summary>
    /// One-time launch sequence: restore saved preferences (so the UI comes up in the user's
    /// chosen language) before loading the profile list. Called once from App startup.
    /// </summary>
    public async Task InitializeAsync()
    {
        await LoadSettingsAsync().ConfigureAwait(true);
        await LoadProfilesAsync().ConfigureAwait(true);
    }

    private async Task LoadSettingsAsync()
    {
        if (_settingsStore is null)
            return;

        try
        {
            _settings = await _settingsStore.LoadAsync().ConfigureAwait(true);
        }
        catch
        {
            // A corrupt or unreadable settings file must not block startup — fall back to defaults.
            _settings = new AppSettings();
            return;
        }

        if (_settings.Language is not { } saved)
            return;

        // Only apply a saved language we still ship; ignore a stale culture from an older build.
        var match = Localization.AvailableLanguages
            .FirstOrDefault(c => string.Equals(c.Name, saved, StringComparison.OrdinalIgnoreCase));
        if (match is not null &&
            !string.Equals(match.Name, Localization.CurrentLanguage.Name, StringComparison.OrdinalIgnoreCase))
        {
            // Apply through the service directly (not the SelectedLanguage setter) so restoring a
            // saved choice doesn't turn around and re-write the same value back to disk.
            Localization.SetLanguage(match);
        }
    }

    private async Task PersistSettingsAsync()
    {
        if (_settingsStore is null)
            return;

        try
        {
            await _settingsStore.SaveAsync(_settings).ConfigureAwait(true);
        }
        catch
        {
            // Persisting a preference is best-effort; a write failure shouldn't disrupt the UI.
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

        // Auto-update check fires once per app launch when there's a real install
        // backing it. In dev runs IsInstalled is false and this is a no-op.
        _ = CheckForUpdatesAsync();
    }

    public UpdateInfo? AvailableUpdate
    {
        get => _availableUpdate;
        private set
        {
            if (SetField(ref _availableUpdate, value))
            {
                RaisePropertyChanged(nameof(HasAvailableUpdate));
                RaisePropertyChanged(nameof(AvailableUpdateText));
                InstallUpdateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasAvailableUpdate => _availableUpdate is not null;
    public string AvailableUpdateText =>
        _availableUpdate is null ? "" : $"Wartownik {_availableUpdate.TargetVersion} is available";

    public bool IsApplyingUpdate
    {
        get => _isApplyingUpdate;
        private set
        {
            if (SetField(ref _isApplyingUpdate, value))
                InstallUpdateCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Background-check the GitHub release feed and surface the result via AvailableUpdate.
    /// Failures (offline, GitHub down, rate-limited) are swallowed — the user sees no banner
    /// and can keep working; we'll try again on next launch.
    /// </summary>
    public async Task CheckForUpdatesAsync()
    {
        if (_updates is null || !_updates.IsInstalled)
            return;
        try
        {
            AvailableUpdate = await _updates.CheckForUpdatesAsync().ConfigureAwait(true);
        }
        catch
        {
            // ignore — non-fatal
        }
    }

    /// <summary>
    /// Download + apply the staged update, then restart the app. The user explicitly clicked
    /// Install, so the restart is part of the contract — no separate confirmation.
    /// </summary>
    private async Task InstallUpdateAsync()
    {
        if (_updates is null || _availableUpdate is null)
            return;
        IsApplyingUpdate = true;
        try
        {
            await _updates.DownloadAsync(_availableUpdate).ConfigureAwait(true);
            _updates.ApplyAndRestart(_availableUpdate);
        }
        catch
        {
            // If anything went wrong, drop the banner so we don't show a half-broken state.
            AvailableUpdate = null;
        }
        finally
        {
            IsApplyingUpdate = false;
        }
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

    /// <summary>Transient message under the profile list — import result / errors.</summary>
    public string? StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetField(ref _statusMessage, value))
                RaisePropertyChanged(nameof(HasStatus));
        }
    }

    public bool HasStatus => !string.IsNullOrEmpty(_statusMessage);

    private async Task AddProfileAsync()
    {
        var result = await _editor.AddAsync().ConfigureAwait(true);
        if (result is null)
            return;

        await _profiles.SaveAsync(result.Profile, result.Password).ConfigureAwait(true);
        await LoadProfilesAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Import one or more profiles from a dropped/opened JSON file's contents. Imported
    /// profiles get a fresh Id and no saved password — the user edits them to add credentials.
    /// </summary>
    public async Task ImportProfilesFromJsonAsync(string json)
    {
        if (!ConnectionProfileJson.TryParse(json, out var imported, out var error))
        {
            var failTemplate = Localization["Profiles.ImportFailedFormat"];
            StatusMessage = string.Format(
                string.IsNullOrEmpty(failTemplate) ? "Import failed: {0}" : failTemplate, error);
            return;
        }

        foreach (var profile in imported)
            await _profiles.SaveAsync(profile, "").ConfigureAwait(true);

        await LoadProfilesAsync().ConfigureAwait(true);
        var okTemplate = Localization["Profiles.ImportedFormat"];
        StatusMessage = string.Format(
            string.IsNullOrEmpty(okTemplate) ? "Imported {0} profile(s). Edit each to add its password." : okTemplate,
            imported.Count);
    }

    private async Task ExportProfileAsync(object? parameter)
    {
        if (_profileExport is null || parameter is not ConnectionProfileItemViewModel item)
            return;

        var json = ConnectionProfileJson.Serialize(item.Profile);
        var fileName = $"{Sanitize(item.Profile.DisplayName)}.json";
        await _profileExport.ExportAsync(fileName, json).ConfigureAwait(true);
    }

    private static string Sanitize(string input) =>
        string.Concat(input.Select(c => char.IsLetterOrDigit(c) ? c : '_'));

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
        IsViewingSettings = false;
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
