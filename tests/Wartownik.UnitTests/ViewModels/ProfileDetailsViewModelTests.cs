using System.Globalization;
using Wartownik.Connections;
using Wartownik.Localization;
using Wartownik.ViewModels;

namespace Wartownik.UnitTests.ViewModels;

public class ProfileDetailsViewModelTests
{
    private static readonly CultureInfo English = new("en");

    private static ConnectionProfile SampleProfile() =>
        ConnectionProfile.Create(
            displayName: "Local",
            host: "localhost",
            port: 5432,
            database: "postgres",
            username: "alice",
            sslMode: PostgresSslMode.Disable);

    private static ProfileDetailsViewModel Create(
        FakeProfileService? profiles = null,
        FakeMetadataService? metadata = null)
    {
        var loc = new LocalizationService(
            new EmptyResources(),
            new[] { English },
            English);
        return new ProfileDetailsViewModel(
            SampleProfile(),
            loc,
            profiles ?? new FakeProfileService(),
            metadata ?? new FakeMetadataService());
    }

    [Fact]
    public async Task LoadAsync_populates_databases_and_clears_loading_state()
    {
        var metadata = new FakeMetadataService(new[] { "alpha", "beta", "gamma" });
        var sut = Create(metadata: metadata);

        await sut.LoadAsync();

        Assert.False(sut.IsLoading);
        Assert.Null(sut.ErrorMessage);
        Assert.Equal(3, sut.Databases.Count);
        Assert.Equal(new[] { "alpha", "beta", "gamma" }, sut.Databases.Select(d => d.Name));
        Assert.True(sut.HasDatabases);
        Assert.False(sut.IsEmpty);
    }

    [Fact]
    public async Task LoadAsync_marks_empty_when_no_databases_returned()
    {
        var metadata = new FakeMetadataService(Array.Empty<string>());
        var sut = Create(metadata: metadata);

        await sut.LoadAsync();

        Assert.False(sut.HasDatabases);
        Assert.True(sut.IsEmpty);
    }

    [Fact]
    public async Task LoadAsync_sets_error_message_when_metadata_throws()
    {
        var metadata = new FakeMetadataService(_ => throw new InvalidOperationException("boom"));
        var sut = Create(metadata: metadata);

        await sut.LoadAsync();

        Assert.False(sut.IsLoading);
        Assert.Equal("boom", sut.ErrorMessage);
        Assert.True(sut.HasError);
        Assert.Empty(sut.Databases);
    }

    [Fact]
    public async Task LoadAsync_passes_password_from_profile_service_to_metadata()
    {
        var profiles = new FakeProfileService();
        profiles.SavedPasswords[SampleProfile().Id] = "secret123";
        // FakeProfileService key not matching SampleProfile().Id since each call returns new profile;
        // Use the actual VM's profile id:
        var loc = new LocalizationService(new EmptyResources(), new[] { English }, English);
        var profile = SampleProfile();
        profiles.SavedPasswords[profile.Id] = "secret123";

        var metadata = new FakeMetadataService(new[] { "x" });
        var sut = new ProfileDetailsViewModel(profile, loc, profiles, metadata);

        await sut.LoadAsync();

        Assert.Equal("secret123", metadata.LastPassword);
        Assert.Equal(profile.Id, metadata.LastProfile?.Id);
    }

    [Fact]
    public async Task LoadAsync_uses_empty_string_password_when_credential_not_found()
    {
        var profiles = new FakeProfileService(); // no passwords saved
        var metadata = new FakeMetadataService(new[] { "x" });
        var sut = Create(profiles: profiles, metadata: metadata);

        await sut.LoadAsync();

        Assert.Equal("", metadata.LastPassword);
    }

    [Fact]
    public async Task LoadAsync_replaces_previous_state_on_subsequent_calls()
    {
        var metadata = new FakeMetadataService(new[] { "first" });
        var sut = Create(metadata: metadata);
        await sut.LoadAsync();

        metadata.SetResult(new[] { "second", "third" });
        await sut.LoadAsync();

        Assert.Equal(2, sut.Databases.Count);
        Assert.Equal(new[] { "second", "third" }, sut.Databases.Select(d => d.Name));
    }

    [Fact]
    public void Endpoint_combines_profile_fields()
    {
        var sut = Create();

        Assert.Equal("localhost:5432 / postgres / alice", sut.Endpoint);
    }

    [Fact]
    public void Constructor_throws_on_null_arguments()
    {
        var loc = new LocalizationService(new EmptyResources(), new[] { English }, English);
        var profile = SampleProfile();
        var profiles = new FakeProfileService();
        var metadata = new FakeMetadataService();

        Assert.Throws<ArgumentNullException>(() => new ProfileDetailsViewModel(null!, loc, profiles, metadata));
        Assert.Throws<ArgumentNullException>(() => new ProfileDetailsViewModel(profile, null!, profiles, metadata));
        Assert.Throws<ArgumentNullException>(() => new ProfileDetailsViewModel(profile, loc, null!, metadata));
        Assert.Throws<ArgumentNullException>(() => new ProfileDetailsViewModel(profile, loc, profiles, null!));
    }

    private sealed class EmptyResources : IStringResources
    {
        public string? Get(string key, CultureInfo culture) => null;
    }

    private sealed class FakeProfileService : IConnectionProfileService
    {
        public Dictionary<Guid, string> SavedPasswords { get; } = new();

        public Task<IReadOnlyList<ConnectionProfile>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConnectionProfile>>(Array.Empty<ConnectionProfile>());

        public Task<ConnectionProfile?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<ConnectionProfile?>(null);

        public Task<string?> GetPasswordAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(SavedPasswords.TryGetValue(id, out var pwd) ? pwd : null);

        public Task SaveAsync(ConnectionProfile profile, string password, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class FakeMetadataService : IPostgresMetadataService
    {
        private Func<ConnectionProfile, IReadOnlyList<DatabaseSummary>> _resolver;
        public ConnectionProfile? LastProfile { get; private set; }
        public string? LastPassword { get; private set; }

        public FakeMetadataService()
            : this(Array.Empty<string>())
        {
        }

        public FakeMetadataService(IReadOnlyList<string> names)
        {
            _resolver = _ => names.Select(n => new DatabaseSummary(n)).ToList();
        }

        public FakeMetadataService(Func<ConnectionProfile, IReadOnlyList<DatabaseSummary>> resolver)
        {
            _resolver = resolver;
        }

        public void SetResult(IReadOnlyList<string> names) =>
            _resolver = _ => names.Select(n => new DatabaseSummary(n)).ToList();

        public Task<IReadOnlyList<DatabaseSummary>> ListDatabasesAsync(
            ConnectionProfile profile,
            string password,
            CancellationToken cancellationToken = default)
        {
            LastProfile = profile;
            LastPassword = password;
            return Task.FromResult(_resolver(profile));
        }
    }
}
