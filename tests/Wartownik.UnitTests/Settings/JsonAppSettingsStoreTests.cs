using Wartownik.Settings;

namespace Wartownik.UnitTests.Settings;

public class JsonAppSettingsStoreTests : IDisposable
{
    private readonly string _filePath = Path.Combine(
        Path.GetTempPath(), $"wartownik-settings-test-{Guid.NewGuid():N}.json");

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
        Assert.ThrowsAny<ArgumentException>(() => new JsonAppSettingsStore(""));
        Assert.ThrowsAny<ArgumentException>(() => new JsonAppSettingsStore("   "));
        Assert.ThrowsAny<ArgumentException>(() => new JsonAppSettingsStore(null!));
    }

    [Fact]
    public async Task LoadAsync_returns_defaults_when_file_does_not_exist()
    {
        var store = new JsonAppSettingsStore(_filePath);

        var settings = await store.LoadAsync();

        Assert.Null(settings.Language);
    }

    [Fact]
    public async Task SaveAsync_then_LoadAsync_round_trips_language()
    {
        var store = new JsonAppSettingsStore(_filePath);

        await store.SaveAsync(new AppSettings { Language = "pl" });
        var loaded = await store.LoadAsync();

        Assert.Equal("pl", loaded.Language);
    }

    [Fact]
    public async Task SaveAsync_creates_the_data_directory_if_missing()
    {
        var nested = Path.Combine(
            Path.GetTempPath(), $"wartownik-settings-dir-{Guid.NewGuid():N}", "settings.json");
        try
        {
            var store = new JsonAppSettingsStore(nested);

            await store.SaveAsync(new AppSettings { Language = "en" });

            Assert.True(File.Exists(nested));
            Assert.Equal("en", (await store.LoadAsync()).Language);
        }
        finally
        {
            var dir = Path.GetDirectoryName(nested)!;
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task SaveAsync_overwrites_previous_settings()
    {
        var store = new JsonAppSettingsStore(_filePath);

        await store.SaveAsync(new AppSettings { Language = "en" });
        await store.SaveAsync(new AppSettings { Language = "pl" });

        Assert.Equal("pl", (await store.LoadAsync()).Language);
    }

    [Fact]
    public async Task LoadAsync_returns_defaults_when_file_is_blank()
    {
        await File.WriteAllTextAsync(_filePath, "   ");
        var store = new JsonAppSettingsStore(_filePath);

        var settings = await store.LoadAsync();

        Assert.Null(settings.Language);
    }

    [Fact]
    public async Task LoadAsync_throws_on_a_malformed_file()
    {
        // Documents the contract the caller relies on: a truncated/corrupt settings.json surfaces
        // as an exception here, and MainWindowViewModel.LoadSettingsAsync is what swallows it so
        // startup still succeeds. If this ever stops throwing, that guard can be revisited.
        await File.WriteAllTextAsync(_filePath, "{ \"language\": ");
        var store = new JsonAppSettingsStore(_filePath);

        await Assert.ThrowsAnyAsync<Exception>(() => store.LoadAsync());
    }

    [Fact]
    public async Task LoadAsync_tolerates_unknown_properties_from_a_newer_build()
    {
        // A settings.json written by a future version with extra keys must still load, keeping the
        // properties this version understands and ignoring the rest.
        await File.WriteAllTextAsync(_filePath, """{ "language": "pl", "futureThing": 42 }""");
        var store = new JsonAppSettingsStore(_filePath);

        var settings = await store.LoadAsync();

        Assert.Equal("pl", settings.Language);
    }
}
