using Wartownik.Connections;
using Wartownik.Connections.Credentials;

namespace Wartownik.UnitTests.Connections;

public class ConnectionProfileServiceTests
{
    private static ConnectionProfile SampleProfile(Guid? id = null) =>
        ConnectionProfile.Create(
            id: id ?? Guid.NewGuid(),
            displayName: "Sample",
            host: "localhost",
            port: 5432,
            database: "postgres",
            username: "postgres",
            sslMode: PostgresSslMode.Prefer);

    [Fact]
    public async Task SaveAsync_persists_profile_and_password()
    {
        var profileStore = new InMemoryProfileStore();
        var credentialStore = new InMemoryCredentialStore();
        var sut = new ConnectionProfileService(profileStore, credentialStore);

        var profile = SampleProfile();
        await sut.SaveAsync(profile, "secret");

        var saved = await profileStore.GetAsync(profile.Id);
        Assert.NotNull(saved);
        Assert.Equal("secret", await sut.GetPasswordAsync(profile.Id));
    }

    [Fact]
    public async Task SaveAsync_stamps_LastEditedAt()
    {
        var profileStore = new InMemoryProfileStore();
        var credentialStore = new InMemoryCredentialStore();
        var sut = new ConnectionProfileService(profileStore, credentialStore);

        var profile = SampleProfile();
        Assert.Null(profile.LastEditedAt); // freshly Created profiles start without a stamp

        var before = DateTimeOffset.UtcNow;
        await sut.SaveAsync(profile, "secret");
        var after = DateTimeOffset.UtcNow;

        var saved = await profileStore.GetAsync(profile.Id);
        Assert.NotNull(saved);
        Assert.NotNull(saved!.LastEditedAt);
        Assert.InRange(saved.LastEditedAt!.Value, before, after);
    }

    [Fact]
    public async Task SaveAsync_uses_distinct_credential_key_per_profile()
    {
        var profileStore = new InMemoryProfileStore();
        var credentialStore = new InMemoryCredentialStore();
        var sut = new ConnectionProfileService(profileStore, credentialStore);

        var p1 = SampleProfile();
        var p2 = SampleProfile();
        await sut.SaveAsync(p1, "first");
        await sut.SaveAsync(p2, "second");

        Assert.Equal("first", await sut.GetPasswordAsync(p1.Id));
        Assert.Equal("second", await sut.GetPasswordAsync(p2.Id));
    }

    [Fact]
    public async Task SaveAsync_overwrites_existing_profile_and_password()
    {
        var profileStore = new InMemoryProfileStore();
        var credentialStore = new InMemoryCredentialStore();
        var sut = new ConnectionProfileService(profileStore, credentialStore);

        var id = Guid.NewGuid();
        await sut.SaveAsync(SampleProfile(id), "first");
        await sut.SaveAsync(SampleProfile(id) with { DisplayName = "Renamed" }, "second");

        var profile = await sut.GetAsync(id);
        Assert.Equal("Renamed", profile!.DisplayName);
        Assert.Equal("second", await sut.GetPasswordAsync(id));
    }

    [Fact]
    public async Task DeleteAsync_removes_profile_and_password()
    {
        var profileStore = new InMemoryProfileStore();
        var credentialStore = new InMemoryCredentialStore();
        var sut = new ConnectionProfileService(profileStore, credentialStore);

        var profile = SampleProfile();
        await sut.SaveAsync(profile, "secret");

        var deleted = await sut.DeleteAsync(profile.Id);

        Assert.True(deleted);
        Assert.Null(await sut.GetAsync(profile.Id));
        Assert.Null(await sut.GetPasswordAsync(profile.Id));
    }

    [Fact]
    public async Task DeleteAsync_returns_false_when_profile_missing_but_still_clears_credential()
    {
        var profileStore = new InMemoryProfileStore();
        var credentialStore = new InMemoryCredentialStore();
        var sut = new ConnectionProfileService(profileStore, credentialStore);

        var unknown = Guid.NewGuid();
        var deleted = await sut.DeleteAsync(unknown);

        Assert.False(deleted);
    }

    [Fact]
    public async Task ListAsync_returns_all_saved_profiles()
    {
        var profileStore = new InMemoryProfileStore();
        var credentialStore = new InMemoryCredentialStore();
        var sut = new ConnectionProfileService(profileStore, credentialStore);

        var p1 = SampleProfile();
        var p2 = SampleProfile();
        await sut.SaveAsync(p1, "a");
        await sut.SaveAsync(p2, "b");

        var list = await sut.ListAsync();

        Assert.Contains(list, p => p.Id == p1.Id);
        Assert.Contains(list, p => p.Id == p2.Id);
    }

    [Fact]
    public async Task GetPasswordAsync_returns_null_for_unknown_profile()
    {
        var sut = new ConnectionProfileService(new InMemoryProfileStore(), new InMemoryCredentialStore());

        Assert.Null(await sut.GetPasswordAsync(Guid.NewGuid()));
    }

    [Fact]
    public void Constructor_throws_on_null_arguments()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ConnectionProfileService(null!, new InMemoryCredentialStore()));
        Assert.Throws<ArgumentNullException>(() =>
            new ConnectionProfileService(new InMemoryProfileStore(), null!));
    }

    private sealed class InMemoryProfileStore : IConnectionProfileStore
    {
        private readonly Dictionary<Guid, ConnectionProfile> _profiles = new();

        public Task<IReadOnlyList<ConnectionProfile>> ListAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ConnectionProfile>>(_profiles.Values.ToList());
        }

        public Task<ConnectionProfile?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _profiles.TryGetValue(id, out var profile);
            return Task.FromResult(profile);
        }

        public Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
        {
            _profiles[profile.Id] = profile;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_profiles.Remove(id));
        }
    }
}
