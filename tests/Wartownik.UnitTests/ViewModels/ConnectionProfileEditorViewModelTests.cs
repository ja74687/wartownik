using System.Globalization;
using Wartownik.Connections;
using Wartownik.Localization;
using Wartownik.ViewModels;

namespace Wartownik.UnitTests.ViewModels;

public class ConnectionProfileEditorViewModelTests
{
    private static readonly CultureInfo English = new("en");

    private static ConnectionProfileEditorViewModel Create(
        IStringResources? resources = null,
        IConnectionTester? tester = null) =>
        new(new LocalizationService(
                resources ?? new EmptyResources(),
                new[] { English, new CultureInfo("pl") },
                English),
            tester ?? new StubTester(ConnectionTestResult.Ok()));

    private static ConnectionProfile SampleProfile() =>
        ConnectionProfile.Create(
            displayName: "Existing",
            host: "db.example.com",
            port: 5433,
            database: "prod",
            username: "alice",
            sslMode: PostgresSslMode.Require);

    [Fact]
    public void Default_state_has_default_port_and_require_ssl_and_is_not_edit_mode()
    {
        var sut = Create();

        Assert.Equal(ConnectionProfile.DefaultPort, sut.Port);
        Assert.Equal(PostgresSslMode.Require, sut.SslMode);
        Assert.False(sut.IsEditMode);
        Assert.Null(sut.ErrorMessage);
    }

    [Fact]
    public void TryBuild_returns_profile_when_fields_valid()
    {
        var sut = Create();
        sut.DisplayName = "Local";
        sut.Host = "localhost";
        sut.Port = 5432;
        sut.Database = "mydb";
        sut.Username = "alice";
        sut.Password = "secret";
        sut.SslMode = PostgresSslMode.Disable;

        Assert.True(sut.TryBuild(out var profile, out var password));
        Assert.Equal("Local", profile.DisplayName);
        Assert.Equal(PostgresSslMode.Disable, profile.SslMode);
        Assert.Equal("secret", password);
    }

    [Fact]
    public void TryBuild_sets_error_message_when_fields_invalid()
    {
        var sut = Create();
        sut.Host = "localhost";

        Assert.False(sut.TryBuild(out _, out _));
        Assert.False(string.IsNullOrEmpty(sut.ErrorMessage));
    }

    [Fact]
    public void LoadFrom_populates_fields_and_marks_edit_mode()
    {
        var sut = Create();
        var profile = SampleProfile();

        sut.LoadFrom(profile, "secret");

        Assert.True(sut.IsEditMode);
        Assert.Equal(profile.DisplayName, sut.DisplayName);
        Assert.Equal(profile.Host, sut.Host);
        Assert.Equal(profile.Port, sut.Port);
        Assert.Equal(profile.Database, sut.Database);
        Assert.Equal(profile.Username, sut.Username);
        Assert.Equal(profile.SslMode, sut.SslMode);
        Assert.Equal("secret", sut.Password);
    }

    [Fact]
    public void TryBuild_in_edit_mode_preserves_existing_id()
    {
        var sut = Create();
        var original = SampleProfile();
        sut.LoadFrom(original, "secret");
        sut.DisplayName = "Renamed";

        Assert.True(sut.TryBuild(out var rebuilt, out _));
        Assert.Equal(original.Id, rebuilt.Id);
        Assert.Equal("Renamed", rebuilt.DisplayName);
    }

    [Fact]
    public void Title_uses_New_key_in_add_mode_and_Edit_key_in_edit_mode()
    {
        var resources = new MapResources()
            .With("Profile.New", "Add new")
            .With("Profile.Edit", "Edit existing");
        var sut = Create(resources);

        Assert.Equal("Add new", sut.Title);

        sut.LoadFrom(SampleProfile(), "secret");
        Assert.Equal("Edit existing", sut.Title);
    }

    [Fact]
    public void Title_changes_when_language_switches()
    {
        var resources = new MapResources()
            .With("Profile.New", English, "New")
            .With("Profile.New", new CultureInfo("pl"), "Nowy");
        var loc = new LocalizationService(
            resources,
            new[] { English, new CultureInfo("pl") },
            English);
        var sut = new ConnectionProfileEditorViewModel(loc, new StubTester(ConnectionTestResult.Ok()));

        var changes = new List<string?>();
        sut.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        loc.SetLanguage(new CultureInfo("pl"));

        Assert.Contains(nameof(ConnectionProfileEditorViewModel.Title), changes);
        Assert.Equal("Nowy", sut.Title);
    }

    [Fact]
    public async Task TestCommand_when_fields_invalid_does_not_call_tester()
    {
        var tester = new StubTester(ConnectionTestResult.Ok());
        var sut = Create(tester: tester);
        // No fields filled - TryBuild fails

        await sut.TestCommand.ExecuteAsync();

        Assert.False(tester.WasCalled);
        Assert.Equal(ConnectionTestState.Idle, sut.TestState);
        Assert.False(string.IsNullOrEmpty(sut.ErrorMessage));
    }

    [Fact]
    public async Task TestCommand_when_tester_returns_ok_marks_success()
    {
        var sut = Create(tester: new StubTester(ConnectionTestResult.Ok()));
        FillValidFields(sut);

        await sut.TestCommand.ExecuteAsync();

        Assert.Equal(ConnectionTestState.Success, sut.TestState);
        Assert.True(sut.TestSucceeded);
        Assert.Null(sut.TestErrorMessage);
    }

    [Fact]
    public async Task TestCommand_when_tester_returns_failure_marks_failed_with_message()
    {
        var sut = Create(tester: new StubTester(ConnectionTestResult.Failure("auth failed")));
        FillValidFields(sut);

        await sut.TestCommand.ExecuteAsync();

        Assert.Equal(ConnectionTestState.Failed, sut.TestState);
        Assert.True(sut.TestFailed);
        Assert.Equal("auth failed", sut.TestErrorMessage);
    }

    [Fact]
    public async Task Editing_field_after_test_resets_state_to_idle()
    {
        var sut = Create(tester: new StubTester(ConnectionTestResult.Ok()));
        FillValidFields(sut);
        await sut.TestCommand.ExecuteAsync();
        Assert.Equal(ConnectionTestState.Success, sut.TestState);

        sut.Host = "different.host";

        Assert.Equal(ConnectionTestState.Idle, sut.TestState);
    }

    [Fact]
    public void Constructor_throws_on_null_arguments()
    {
        var tester = new StubTester(ConnectionTestResult.Ok());
        var loc = new LocalizationService(new EmptyResources(), new[] { English }, English);
        Assert.Throws<ArgumentNullException>(() => new ConnectionProfileEditorViewModel(null!, tester));
        Assert.Throws<ArgumentNullException>(() => new ConnectionProfileEditorViewModel(loc, null!));
    }

    private static void FillValidFields(ConnectionProfileEditorViewModel sut)
    {
        sut.DisplayName = "Local";
        sut.Host = "localhost";
        sut.Port = 5432;
        sut.Database = "mydb";
        sut.Username = "alice";
        sut.Password = "secret";
        sut.SslMode = PostgresSslMode.Disable;
    }

    private sealed class StubTester : IConnectionTester
    {
        private readonly ConnectionTestResult _result;
        public bool WasCalled { get; private set; }

        public StubTester(ConnectionTestResult result) => _result = result;

        public Task<ConnectionTestResult> TestAsync(
            ConnectionProfile profile,
            string password,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(_result);
        }
    }

    [Fact]
    public void LoadFrom_throws_on_null_arguments()
    {
        var sut = Create();
        Assert.Throws<ArgumentNullException>(() => sut.LoadFrom(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => sut.LoadFrom(SampleProfile(), null!));
    }

    private sealed class EmptyResources : IStringResources
    {
        public string? Get(string key, CultureInfo culture) => null;
    }

    private sealed class MapResources : IStringResources
    {
        private readonly Dictionary<(string Key, string? CultureName), string> _entries = new();

        public MapResources With(string key, string value)
        {
            _entries[(key, null)] = value;
            return this;
        }

        public MapResources With(string key, CultureInfo culture, string value)
        {
            _entries[(key, culture.Name)] = value;
            return this;
        }

        public string? Get(string key, CultureInfo culture)
        {
            if (_entries.TryGetValue((key, culture.Name), out var value))
                return value;
            return _entries.TryGetValue((key, null), out value) ? value : null;
        }
    }
}
