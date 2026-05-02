using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Wartownik.Connections;
using Wartownik.Dialogs;
using Wartownik.Localization;

namespace Wartownik.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly IConnectionProfileService _profiles;
    private readonly IConnectionProfileEditor _editor;
    private readonly IConfirmationDialog _confirmation;
    private readonly IPostgresMetadataService _metadata;

    private ProfileDetailsViewModel? _details;

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
        IPostgresMetadataService metadata)
    {
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(confirmation);
        ArgumentNullException.ThrowIfNull(metadata);

        Localization = localization;
        _profiles = profiles;
        _editor = editor;
        _confirmation = confirmation;
        _metadata = metadata;

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
            if (SetField(ref _details, value))
                RaisePropertyChanged(nameof(IsViewingDetails));
        }
    }

    public bool IsViewingDetails => _details is not null;

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

    public async Task LoadProfilesAsync()
    {
        var loaded = await _profiles.ListAsync().ConfigureAwait(true);
        Profiles.Clear();
        foreach (var profile in loaded)
            Profiles.Add(new ConnectionProfileItemViewModel(profile));
        RaisePropertyChanged(nameof(HasProfiles));
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

        var details = new ProfileDetailsViewModel(item.Profile, Localization, _profiles, _metadata);
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
    }
}
