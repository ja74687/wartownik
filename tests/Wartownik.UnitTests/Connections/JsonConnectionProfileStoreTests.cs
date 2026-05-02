using Wartownik.Connections;

namespace Wartownik.UnitTests.Connections;

public class JsonConnectionProfileStoreTests : IDisposable
{
    private readonly string _filePath = Path.Combine(
        Path.GetTempPath(), $"wartownik-test-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        TryDelete(_filePath);
        TryDelete(_filePath + ".tmp");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* test cleanup is best-effort */ }
    }

    [Fact]
    public void Constructor_rejects_blank_path()
    {
        Assert.ThrowsAny<ArgumentException>(() => new JsonConnectionProfileStore(""));
        Assert.ThrowsAny<ArgumentException>(() => new JsonConnectionProfileStore("   "));
        Assert.ThrowsAny<ArgumentException>(() => new JsonConnectionProfileStore(null!));
    }

    [Fact]
    public async Task ListAsync_returns_empty_when_file_does_not_exist()
    {
        var store = new JsonConnectionProfileStore(_filePath);
        var profiles = await store.ListAsync();
        Assert.Empty(profiles);
    }

    [Fact]
    public async Task SaveAsync_then_ListAsync_round_trips_profile()
    {
        var store = new JsonConnectionProfileStore(_filePath);
        var profile = ConnectionProfile.Create("dev", "localhost", 5432, "mydb", "alice");

        await store.SaveAsync(profile);
        var profiles = await store.ListAsync();

        var loaded = Assert.Single(profiles);
        Assert.Equal(profile, loaded);
    }

    [Fact]
    public async Task SaveAsync_with_existing_id_updates_in_place()
    {
        var store = new JsonConnectionProfileStore(_filePath);
        var id = Guid.NewGuid();
        var original = ConnectionProfile.Create(id, "dev", "localhost", 5432, "mydb", "alice", PostgresSslMode.Require);
        var modified = ConnectionProfile.Create(id, "prod", "db.example.com", 5433, "otherdb", "bob", PostgresSslMode.VerifyFull);

        await store.SaveAsync(original);
        await store.SaveAsync(modified);
        var profiles = await store.ListAsync();

        var loaded = Assert.Single(profiles);
        Assert.Equal(modified, loaded);
    }

    [Fact]
    public async Task SaveAsync_appends_distinct_profiles()
    {
        var store = new JsonConnectionProfileStore(_filePath);
        var p1 = ConnectionProfile.Create("dev", "localhost", 5432, "mydb", "alice");
        var p2 = ConnectionProfile.Create("prod", "db.example.com", 5433, "otherdb", "bob");

        await store.SaveAsync(p1);
        await store.SaveAsync(p2);
        var profiles = await store.ListAsync();

        Assert.Equal(2, profiles.Count);
        Assert.Contains(p1, profiles);
        Assert.Contains(p2, profiles);
    }

    [Fact]
    public async Task GetAsync_returns_profile_by_id()
    {
        var store = new JsonConnectionProfileStore(_filePath);
        var profile = ConnectionProfile.Create("dev", "localhost", 5432, "mydb", "alice");
        await store.SaveAsync(profile);

        var fetched = await store.GetAsync(profile.Id);

        Assert.Equal(profile, fetched);
    }

    [Fact]
    public async Task GetAsync_returns_null_when_not_found()
    {
        var store = new JsonConnectionProfileStore(_filePath);
        var fetched = await store.GetAsync(Guid.NewGuid());
        Assert.Null(fetched);
    }

    [Fact]
    public async Task DeleteAsync_removes_existing_profile()
    {
        var store = new JsonConnectionProfileStore(_filePath);
        var profile = ConnectionProfile.Create("dev", "localhost", 5432, "mydb", "alice");
        await store.SaveAsync(profile);

        var removed = await store.DeleteAsync(profile.Id);

        Assert.True(removed);
        Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public async Task DeleteAsync_returns_false_when_not_found()
    {
        var store = new JsonConnectionProfileStore(_filePath);
        var removed = await store.DeleteAsync(Guid.NewGuid());
        Assert.False(removed);
    }

    [Fact]
    public async Task DeleteAsync_keeps_other_profiles_intact()
    {
        var store = new JsonConnectionProfileStore(_filePath);
        var p1 = ConnectionProfile.Create("dev", "localhost", 5432, "mydb", "alice");
        var p2 = ConnectionProfile.Create("prod", "db.example.com", 5433, "otherdb", "bob");
        await store.SaveAsync(p1);
        await store.SaveAsync(p2);

        await store.DeleteAsync(p1.Id);
        var remaining = await store.ListAsync();

        Assert.Equal(p2, Assert.Single(remaining));
    }

    [Fact]
    public async Task SaveAsync_does_not_leave_temp_file_behind()
    {
        var store = new JsonConnectionProfileStore(_filePath);
        var profile = ConnectionProfile.Create("dev", "localhost", 5432, "mydb", "alice");

        await store.SaveAsync(profile);

        Assert.False(File.Exists(_filePath + ".tmp"), "atomic write should clean up the temp file");
    }

    [Fact]
    public async Task SaveAsync_creates_missing_parent_directory()
    {
        var nestedDir = Path.Combine(Path.GetTempPath(), $"wartownik-test-{Guid.NewGuid():N}", "nested");
        var nestedPath = Path.Combine(nestedDir, "profiles.json");
        try
        {
            var store = new JsonConnectionProfileStore(nestedPath);
            var profile = ConnectionProfile.Create("dev", "localhost", 5432, "mydb", "alice");

            await store.SaveAsync(profile);

            Assert.True(File.Exists(nestedPath));
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(nestedPath))!, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public async Task ListAsync_throws_when_file_contains_corrupt_profile()
    {
        // Profile with port out of range — reading must reject (validation runs on load).
        var corruptJson = """
            {
              "profiles": [
                {
                  "id": "00000000-0000-0000-0000-000000000001",
                  "displayName": "bad",
                  "host": "localhost",
                  "port": 999999,
                  "database": "mydb",
                  "username": "alice",
                  "sslMode": "require"
                }
              ]
            }
            """;
        await File.WriteAllTextAsync(_filePath, corruptJson);
        var store = new JsonConnectionProfileStore(_filePath);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await store.ListAsync());
    }

    [Fact]
    public async Task ListAsync_returns_empty_for_empty_file()
    {
        await File.WriteAllTextAsync(_filePath, "");
        var store = new JsonConnectionProfileStore(_filePath);

        var profiles = await store.ListAsync();

        Assert.Empty(profiles);
    }

    [Fact]
    public async Task Concurrent_saves_do_not_corrupt_file()
    {
        var store = new JsonConnectionProfileStore(_filePath);
        var profiles = Enumerable.Range(0, 20)
            .Select(i => ConnectionProfile.Create($"profile-{i}", "localhost", 5432, "db", "user"))
            .ToList();

        var tasks = profiles.Select(p => store.SaveAsync(p));
        await Task.WhenAll(tasks);

        var loaded = await store.ListAsync();
        Assert.Equal(profiles.Count, loaded.Count);
    }
}
