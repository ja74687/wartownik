using System.Globalization;
using Wartownik.Connections;
using Wartownik.Localization;
using Wartownik.ViewModels;

namespace Wartownik.UnitTests.ViewModels;

public class DatabaseDetailsViewModelTests
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

    private static DatabaseDetailsViewModel Create(
        FakeProfileService? profiles = null,
        FakeMetadataService? metadata = null,
        string dbName = "mydb")
    {
        var loc = new LocalizationService(
            new EmptyResources(),
            new[] { English },
            English);
        return new DatabaseDetailsViewModel(
            SampleProfile(),
            new DatabaseSummary(dbName),
            loc,
            profiles ?? new FakeProfileService(),
            metadata ?? new FakeMetadataService());
    }

    [Fact]
    public async Task LoadAsync_populates_schemas()
    {
        var meta = new FakeMetadataService(new[] { "public", "app" });
        var sut = Create(metadata: meta);

        await sut.LoadAsync();

        Assert.False(sut.IsLoading);
        Assert.Null(sut.ErrorMessage);
        Assert.Equal(2, sut.Schemas.Count);
        Assert.Equal(new[] { "public", "app" }, sut.Schemas.Select(s => s.Name));
        Assert.True(sut.HasSchemas);
        Assert.False(sut.IsSchemasEmpty);
    }

    [Fact]
    public async Task LoadAsync_marks_empty_when_no_schemas_returned()
    {
        var meta = new FakeMetadataService(Array.Empty<string>());
        var sut = Create(metadata: meta);

        await sut.LoadAsync();

        Assert.False(sut.HasSchemas);
        Assert.True(sut.IsSchemasEmpty);
    }

    [Fact]
    public async Task LoadAsync_sets_error_message_when_metadata_throws()
    {
        var meta = new FakeMetadataService(_ => throw new InvalidOperationException("boom"));
        var sut = Create(metadata: meta);

        await sut.LoadAsync();

        Assert.False(sut.IsLoading);
        Assert.Equal("boom", sut.ErrorMessage);
        Assert.True(sut.HasError);
        Assert.Empty(sut.Schemas);
    }

    [Fact]
    public async Task LoadAsync_uses_target_database_name_when_calling_metadata()
    {
        var meta = new FakeMetadataService(new[] { "public" });
        var sut = Create(metadata: meta, dbName: "different_db");

        await sut.LoadAsync();

        Assert.Equal("different_db", meta.LastDatabaseName);
    }

    [Fact]
    public async Task LoadAsync_passes_password_from_profile_service()
    {
        var profile = SampleProfile();
        var profiles = new FakeProfileService();
        profiles.SavedPasswords[profile.Id] = "secret";
        var meta = new FakeMetadataService(new[] { "public" });
        var loc = new LocalizationService(new EmptyResources(), new[] { English }, English);
        var sut = new DatabaseDetailsViewModel(profile, new DatabaseSummary("mydb"), loc, profiles, meta);

        await sut.LoadAsync();

        Assert.Equal("secret", meta.LastPassword);
    }

    [Fact]
    public void Endpoint_uses_database_name_not_profile_database()
    {
        var sut = Create(dbName: "different_db");

        // Profile.Database = "postgres", but endpoint should show the workspace DB
        Assert.Equal("localhost:5432 / different_db / alice", sut.Endpoint);
    }

    [Fact]
    public void Constructor_throws_on_blank_database_name()
    {
        var loc = new LocalizationService(new EmptyResources(), new[] { English }, English);
        Assert.Throws<ArgumentException>(() =>
            new DatabaseDetailsViewModel(SampleProfile(), new DatabaseSummary("  "), loc,
                new FakeProfileService(), new FakeMetadataService()));
    }

    [Fact]
    public void Constructor_throws_on_null_arguments()
    {
        var loc = new LocalizationService(new EmptyResources(), new[] { English }, English);
        var profile = SampleProfile();
        var profiles = new FakeProfileService();
        var meta = new FakeMetadataService();
        var summary = new DatabaseSummary("db");

        Assert.Throws<ArgumentNullException>(() =>
            new DatabaseDetailsViewModel(null!, summary, loc, profiles, meta));
        Assert.Throws<ArgumentNullException>(() =>
            new DatabaseDetailsViewModel(profile, null!, loc, profiles, meta));
        Assert.Throws<ArgumentNullException>(() =>
            new DatabaseDetailsViewModel(profile, summary, null!, profiles, meta));
        Assert.Throws<ArgumentNullException>(() =>
            new DatabaseDetailsViewModel(profile, summary, loc, null!, meta));
        Assert.Throws<ArgumentNullException>(() =>
            new DatabaseDetailsViewModel(profile, summary, loc, profiles, null!));
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
        private readonly Func<string, IReadOnlyList<SchemaSummary>> _resolver;
        public string? LastDatabaseName { get; private set; }
        public string? LastPassword { get; private set; }

        public FakeMetadataService() : this(Array.Empty<string>()) { }

        public FakeMetadataService(IReadOnlyList<string> schemaNames)
        {
            _resolver = _ => schemaNames.Select(n => new SchemaSummary(n)).ToList();
        }

        public FakeMetadataService(Func<string, IReadOnlyList<SchemaSummary>> resolver)
        {
            _resolver = resolver;
        }

        public Task<IReadOnlyList<DatabaseSummary>> ListDatabasesAsync(
            ConnectionProfile profile, string password, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DatabaseSummary>>(Array.Empty<DatabaseSummary>());

        public Task<IReadOnlyList<RoleSummary>> ListRolesAsync(
            ConnectionProfile profile, string password, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RoleSummary>>(Array.Empty<RoleSummary>());

        public Task<IReadOnlyList<SchemaSummary>> ListSchemasAsync(
            ConnectionProfile profile,
            string password,
            string databaseName,
            CancellationToken cancellationToken = default)
        {
            LastDatabaseName = databaseName;
            LastPassword = password;
            return Task.FromResult(_resolver(databaseName));
        }
    }
}
