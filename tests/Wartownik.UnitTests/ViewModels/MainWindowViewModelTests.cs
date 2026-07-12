using System.ComponentModel;
using System.Globalization;
using Wartownik.Connections;
using Wartownik.Dialogs;
using Wartownik.Localization;
using Wartownik.ViewModels;

namespace Wartownik.UnitTests.ViewModels;

public class MainWindowViewModelTests
{
    private static readonly CultureInfo English = new("en");
    private static readonly CultureInfo Polish = new("pl");

    private static (
        MainWindowViewModel Vm,
        ILocalizationService Loc,
        FakeProfileService Profiles,
        FakeEditor Editor,
        FakeConfirmationDialog Confirmation)
        Build(bool defaultConfirm = true, FakeMetadataService? metadata = null)
    {
        var loc = new LocalizationService(
            new EmptyResources(),
            new[] { English, Polish },
            English);
        var profiles = new FakeProfileService();
        var editor = new FakeEditor();
        var confirmation = new FakeConfirmationDialog { NextResult = defaultConfirm };
        var meta = metadata ?? new FakeMetadataService();
        var roleAdmin = new FakeRoleAdminService();
        var roleEditor = new FakeRoleEditor();

        ProfileDetailsViewModel.DatabaseDetailsFactory dbFactory = (p, db) =>
            new DatabaseDetailsViewModel(p, db, loc, profiles, meta);

        MainWindowViewModel.ProfileDetailsFactory factory = profile =>
            new ProfileDetailsViewModel(profile, loc, profiles, meta, roleAdmin, roleEditor, confirmation, dbFactory);

        var tester = new FakeConnectionTester();

        return (new MainWindowViewModel(loc, profiles, editor, confirmation, tester, meta, factory),
            loc, profiles, editor, confirmation);
    }

    private static ConnectionProfile SampleProfile(string name = "Sample") =>
        ConnectionProfile.Create(
            displayName: name,
            host: "localhost",
            port: 5432,
            database: "postgres",
            username: "postgres",
            sslMode: PostgresSslMode.Disable);

    [Fact]
    public void SelectedLanguage_initially_matches_localization_current_language()
    {
        var (vm, loc, _, _, _) = Build();

        Assert.Equal(loc.CurrentLanguage.Name, vm.SelectedLanguage.Name);
    }

    [Fact]
    public void Setting_selected_language_propagates_to_localization_service()
    {
        var (vm, loc, _, _, _) = Build();

        vm.SelectedLanguage = Polish;

        Assert.Equal(Polish.Name, loc.CurrentLanguage.Name);
        Assert.Equal(Polish.Name, vm.SelectedLanguage.Name);
    }

    [Fact]
    public void Setting_selected_language_to_same_value_is_noop()
    {
        var (vm, _, _, _, _) = Build();
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.SelectedLanguage = English;

        Assert.Empty(changes);
    }

    [Fact]
    public void Localization_change_raises_PropertyChanged_for_SelectedLanguage()
    {
        var (vm, loc, _, _, _) = Build();
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        loc.SetLanguage(Polish);

        Assert.Contains(nameof(MainWindowViewModel.SelectedLanguage), changes);
    }

    [Fact]
    public async Task LoadProfilesAsync_populates_collection()
    {
        var (vm, _, profiles, _, _) = Build();
        profiles.Items.Add(SampleProfile("A"));
        profiles.Items.Add(SampleProfile("B"));

        await vm.LoadProfilesAsync();

        Assert.Equal(2, vm.Profiles.Count);
        Assert.True(vm.HasProfiles);
    }

    [Fact]
    public async Task LoadProfilesAsync_replaces_previous_collection_state()
    {
        var (vm, _, profiles, _, _) = Build();
        profiles.Items.Add(SampleProfile("A"));
        await vm.LoadProfilesAsync();

        profiles.Items.Clear();
        profiles.Items.Add(SampleProfile("B"));
        await vm.LoadProfilesAsync();

        Assert.Single(vm.Profiles);
        Assert.Equal("B", vm.Profiles[0].DisplayName);
    }

    [Fact]
    public async Task AddProfileCommand_when_editor_returns_result_saves_and_reloads()
    {
        var (vm, _, profiles, editor, _) = Build();
        var newProfile = SampleProfile("NewOne");
        editor.NextResult = new ConnectionProfileEditResult(newProfile, "pwd");

        await vm.AddProfileCommand.ExecuteAsync();

        Assert.Single(profiles.Items);
        Assert.Equal("pwd", profiles.SavedPasswords[newProfile.Id]);
        Assert.Single(vm.Profiles);
    }

    [Fact]
    public async Task AddProfileCommand_when_editor_cancels_does_nothing()
    {
        var (vm, _, profiles, editor, _) = Build();
        editor.NextResult = null;

        await vm.AddProfileCommand.ExecuteAsync();

        Assert.Empty(profiles.Items);
        Assert.Empty(vm.Profiles);
    }

    [Fact]
    public async Task EditProfileCommand_when_editor_returns_result_saves_and_reloads()
    {
        var (vm, _, profiles, editor, _) = Build();
        var original = SampleProfile("Original");
        profiles.Items.Add(original);
        profiles.SavedPasswords[original.Id] = "old";
        await vm.LoadProfilesAsync();
        var item = vm.Profiles[0];

        var renamed = ConnectionProfile.Create(original.Id, "Renamed", original.Host, original.Port,
            original.Database, original.Username, original.SslMode);
        editor.NextResult = new ConnectionProfileEditResult(renamed, "new");

        await vm.EditProfileCommand.ExecuteAsync(item);

        Assert.Equal(original.Id, editor.LastEditedProfile!.Id);
        Assert.Equal("old", editor.LastEditedPassword);
        Assert.Single(profiles.Items);
        Assert.Equal("Renamed", profiles.Items[0].DisplayName);
        Assert.Equal("new", profiles.SavedPasswords[original.Id]);
    }

    [Fact]
    public async Task EditProfileCommand_when_editor_cancels_does_not_save()
    {
        var (vm, _, profiles, editor, _) = Build();
        var p = SampleProfile();
        profiles.Items.Add(p);
        profiles.SavedPasswords[p.Id] = "x";
        await vm.LoadProfilesAsync();
        editor.NextResult = null;

        await vm.EditProfileCommand.ExecuteAsync(vm.Profiles[0]);

        Assert.Equal("x", profiles.SavedPasswords[p.Id]);
    }

    [Fact]
    public async Task EditProfileCommand_with_invalid_parameter_does_nothing()
    {
        var (vm, _, profiles, editor, _) = Build();
        editor.NextResult = new ConnectionProfileEditResult(SampleProfile("X"), "x");

        await vm.EditProfileCommand.ExecuteAsync("not a viewmodel");

        Assert.Empty(profiles.Items);
        Assert.Null(editor.LastEditedProfile);
    }

    [Fact]
    public async Task DeleteProfileCommand_when_user_confirms_removes_item_and_reloads()
    {
        var (vm, _, profiles, _, confirmation) = Build(defaultConfirm: true);
        var p = SampleProfile();
        profiles.Items.Add(p);
        await vm.LoadProfilesAsync();
        var item = vm.Profiles[0];

        await vm.DeleteProfileCommand.ExecuteAsync(item);

        Assert.True(confirmation.WasAsked);
        Assert.Empty(profiles.Items);
        Assert.Empty(vm.Profiles);
        Assert.False(vm.HasProfiles);
    }

    [Fact]
    public async Task DeleteProfileCommand_when_user_cancels_does_not_delete()
    {
        var (vm, _, profiles, _, confirmation) = Build(defaultConfirm: false);
        var p = SampleProfile();
        profiles.Items.Add(p);
        await vm.LoadProfilesAsync();
        var item = vm.Profiles[0];

        await vm.DeleteProfileCommand.ExecuteAsync(item);

        Assert.True(confirmation.WasAsked);
        Assert.Single(profiles.Items);
        Assert.Single(vm.Profiles);
    }

    [Fact]
    public async Task DeleteProfileCommand_marks_confirmation_as_destructive()
    {
        var (vm, _, profiles, _, confirmation) = Build(defaultConfirm: true);
        profiles.Items.Add(SampleProfile());
        await vm.LoadProfilesAsync();

        await vm.DeleteProfileCommand.ExecuteAsync(vm.Profiles[0]);

        Assert.NotNull(confirmation.LastRequest);
        Assert.True(confirmation.LastRequest!.IsDestructive);
    }

    [Fact]
    public async Task DeleteProfileCommand_with_invalid_parameter_does_nothing()
    {
        var (vm, _, profiles, _, confirmation) = Build();
        profiles.Items.Add(SampleProfile());
        await vm.LoadProfilesAsync();

        await vm.DeleteProfileCommand.ExecuteAsync("not a viewmodel");

        Assert.False(confirmation.WasAsked);
        Assert.Single(profiles.Items);
    }

    [Fact]
    public async Task OpenProfileCommand_sets_details_and_loads_databases()
    {
        var meta = new FakeMetadataService(new[] { "alpha" });
        var (vm, _, profiles, _, _) = Build(metadata: meta);
        var p = SampleProfile();
        profiles.Items.Add(p);
        await vm.LoadProfilesAsync();
        var item = vm.Profiles[0];

        await vm.OpenProfileCommand.ExecuteAsync(item);

        Assert.NotNull(vm.Details);
        Assert.True(vm.IsViewingDetails);
        Assert.Equal(p.Id, vm.Details!.Profile.Id);
        Assert.Single(vm.Details.Databases);
        Assert.Equal("alpha", vm.Details.Databases[0].Name);
    }

    [Fact]
    public async Task OpenProfileCommand_with_invalid_parameter_does_nothing()
    {
        var (vm, _, _, _, _) = Build();

        await vm.OpenProfileCommand.ExecuteAsync("not a viewmodel");

        Assert.Null(vm.Details);
        Assert.False(vm.IsViewingDetails);
    }

    [Fact]
    public async Task BackToProfilesCommand_clears_details()
    {
        var (vm, _, profiles, _, _) = Build();
        profiles.Items.Add(SampleProfile());
        await vm.LoadProfilesAsync();
        await vm.OpenProfileCommand.ExecuteAsync(vm.Profiles[0]);
        Assert.True(vm.IsViewingDetails);

        await vm.BackToProfilesCommand.ExecuteAsync();

        Assert.Null(vm.Details);
        Assert.False(vm.IsViewingDetails);
    }

    [Fact]
    public async Task SearchFilter_filters_profiles_by_display_name_or_endpoint()
    {
        var (vm, _, profiles, _, _) = Build();
        profiles.Items.Add(SampleProfile("Local dev"));
        profiles.Items.Add(SampleProfile("Production"));
        profiles.Items.Add(SampleProfile("Staging"));
        await vm.LoadProfilesAsync();
        Assert.Equal(3, vm.Profiles.Count);

        vm.SearchFilter = "prod";

        Assert.Single(vm.Profiles);
        Assert.Equal("Production", vm.Profiles[0].DisplayName);

        vm.SearchFilter = "";
        Assert.Equal(3, vm.Profiles.Count);
    }

    [Fact]
    public async Task LoadProfilesAsync_kicks_off_meta_refresh_per_item()
    {
        var (vm, _, profiles, _, _) = Build();
        profiles.Items.Add(SampleProfile("A"));

        await vm.LoadProfilesAsync();

        // Background refresh is fire-and-forget; allow it to complete.
        await Task.Delay(50);
        Assert.NotEqual(ConnectionStatus.Unknown, vm.Profiles[0].Status);
    }

    [Fact]
    public void Constructor_throws_on_null_arguments()
    {
        var loc = new LocalizationService(new EmptyResources(), new[] { English }, English);
        var profiles = new FakeProfileService();
        var editor = new FakeEditor();
        var confirmation = new FakeConfirmationDialog();
        var tester = new FakeConnectionTester();
        var meta = new FakeMetadataService();
        ProfileDetailsViewModel.DatabaseDetailsFactory dbFactory = (p, db) =>
            new DatabaseDetailsViewModel(p, db, loc, profiles, meta);
        MainWindowViewModel.ProfileDetailsFactory factory = profile =>
            new ProfileDetailsViewModel(profile, loc, profiles, meta,
                new FakeRoleAdminService(), new FakeRoleEditor(), confirmation, dbFactory);

        Assert.Throws<ArgumentNullException>(() =>
            new MainWindowViewModel(null!, profiles, editor, confirmation, tester, meta, factory));
        Assert.Throws<ArgumentNullException>(() =>
            new MainWindowViewModel(loc, null!, editor, confirmation, tester, meta, factory));
        Assert.Throws<ArgumentNullException>(() =>
            new MainWindowViewModel(loc, profiles, null!, confirmation, tester, meta, factory));
        Assert.Throws<ArgumentNullException>(() =>
            new MainWindowViewModel(loc, profiles, editor, null!, tester, meta, factory));
        Assert.Throws<ArgumentNullException>(() =>
            new MainWindowViewModel(loc, profiles, editor, confirmation, null!, meta, factory));
        Assert.Throws<ArgumentNullException>(() =>
            new MainWindowViewModel(loc, profiles, editor, confirmation, tester, null!, factory));
        Assert.Throws<ArgumentNullException>(() =>
            new MainWindowViewModel(loc, profiles, editor, confirmation, tester, meta, null!));
    }

    // ---------- Profile import ----------

    [Fact]
    public async Task ImportProfilesFromJsonAsync_valid_json_saves_profile_without_password()
    {
        var (vm, _, profiles, _, _) = Build();
        const string json = """
            { "displayName": "Imported dev", "host": "db.local", "port": 5432, "database": "mydb", "username": "svc", "sslMode": "Require" }
            """;

        await vm.ImportProfilesFromJsonAsync(json);

        var saved = Assert.Single(profiles.Items);
        Assert.Equal("Imported dev", saved.DisplayName);
        Assert.Equal("svc", saved.Username);
        Assert.True(vm.HasStatus);
        Assert.Equal("", await profiles.GetPasswordAsync(saved.Id)); // no password on import
    }

    [Fact]
    public async Task ImportProfilesFromJsonAsync_array_saves_all_profiles()
    {
        var (vm, _, profiles, _, _) = Build();
        const string json = """
            [
              { "displayName": "a", "host": "h1", "port": 5432, "database": "d1", "username": "u1" },
              { "displayName": "b", "host": "h2", "port": 5433, "database": "d2", "username": "u2" }
            ]
            """;

        await vm.ImportProfilesFromJsonAsync(json);

        Assert.Equal(2, profiles.Items.Count);
    }

    [Fact]
    public async Task ImportProfilesFromJsonAsync_malformed_json_saves_nothing_and_reports_error()
    {
        var (vm, _, profiles, _, _) = Build();

        await vm.ImportProfilesFromJsonAsync("{ not json");

        Assert.Empty(profiles.Items);
        Assert.True(vm.HasStatus);
    }

    private sealed class EmptyResources : IStringResources
    {
        public string? Get(string key, CultureInfo culture) => null;
    }

    private sealed class FakeProfileService : IConnectionProfileService
    {
        public List<ConnectionProfile> Items { get; } = new();
        public Dictionary<Guid, string> SavedPasswords { get; } = new();

        public Task<IReadOnlyList<ConnectionProfile>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConnectionProfile>>(Items.ToList());

        public Task<ConnectionProfile?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(p => p.Id == id));

        public Task<string?> GetPasswordAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(SavedPasswords.TryGetValue(id, out var pwd) ? pwd : null);

        public Task SaveAsync(ConnectionProfile profile, string password, CancellationToken cancellationToken = default)
        {
            var index = Items.FindIndex(p => p.Id == profile.Id);
            if (index >= 0) Items[index] = profile;
            else Items.Add(profile);
            SavedPasswords[profile.Id] = password;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            SavedPasswords.Remove(id);
            return Task.FromResult(Items.RemoveAll(p => p.Id == id) > 0);
        }
    }

    private sealed class FakeEditor : IConnectionProfileEditor
    {
        public ConnectionProfileEditResult? NextResult { get; set; }
        public ConnectionProfile? LastEditedProfile { get; private set; }
        public string? LastEditedPassword { get; private set; }

        public Task<ConnectionProfileEditResult?> AddAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(NextResult);

        public Task<ConnectionProfileEditResult?> EditAsync(
            ConnectionProfile profile,
            string password,
            CancellationToken cancellationToken = default)
        {
            LastEditedProfile = profile;
            LastEditedPassword = password;
            return Task.FromResult(NextResult);
        }
    }

    private sealed class FakeConfirmationDialog : IConfirmationDialog
    {
        public bool NextResult { get; set; } = true;
        public bool WasAsked { get; private set; }
        public ConfirmationRequest? LastRequest { get; private set; }

        public Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken cancellationToken = default)
        {
            WasAsked = true;
            LastRequest = request;
            return Task.FromResult(NextResult);
        }
    }

    private sealed class FakeMetadataService : IPostgresMetadataService
    {
        private readonly IReadOnlyList<string> _names;

        public FakeMetadataService() : this(Array.Empty<string>()) { }
        public FakeMetadataService(IReadOnlyList<string> names) => _names = names;

        public Task<IReadOnlyList<DatabaseSummary>> ListDatabasesAsync(
            ConnectionProfile profile,
            string password,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DatabaseSummary>>(
                _names.Select(n => new DatabaseSummary(n)).ToList());

        public Task<IReadOnlyList<RoleSummary>> ListRolesAsync(
            ConnectionProfile profile,
            string password,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RoleSummary>>(Array.Empty<RoleSummary>());

        public Task<IReadOnlyList<SchemaSummary>> ListSchemasAsync(
            ConnectionProfile profile,
            string password,
            string databaseName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SchemaSummary>>(Array.Empty<SchemaSummary>());
    }

    private sealed class FakeConnectionTester : IConnectionTester
    {
        public ConnectionTestResult NextResult { get; set; } = ConnectionTestResult.Ok();

        public Task<ConnectionTestResult> TestAsync(
            ConnectionProfile profile,
            string password,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(NextResult);
    }

    private sealed class FakeRoleAdminService : IPostgresRoleAdminService
    {
        public Task CreateRoleAsync(
            ConnectionProfile profile,
            string profilePassword,
            CreateRoleRequest request,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task AlterRoleAsync(
            ConnectionProfile profile,
            string profilePassword,
            AlterRoleRequest request,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DropRoleAsync(
            ConnectionProfile profile,
            string profilePassword,
            string roleName,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeRoleEditor : IRoleEditor
    {
        public Task<CreateRoleRequest?> CreateAsync(bool canLoginDefault = false, CancellationToken cancellationToken = default) =>
            Task.FromResult<CreateRoleRequest?>(null);

        public Task<AlterRoleRequest?> EditAsync(RoleSummary current, CancellationToken cancellationToken = default) =>
            Task.FromResult<AlterRoleRequest?>(null);
    }
}
