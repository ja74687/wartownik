using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wartownik.Settings;

/// <summary>
/// JSON-file-backed <see cref="IAppSettingsStore"/>. Mirrors <see cref="Connections.JsonConnectionProfileStore"/>:
/// a single serialized object at a path resolved by AppPaths, camelCase with string enums,
/// serialized writes guarded by a semaphore, and an atomic temp-file swap so a crash mid-write
/// can't leave a half-written settings.json.
/// </summary>
public sealed class JsonAppSettingsStore : IAppSettingsStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public JsonAppSettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath))
                return new AppSettings();

            var json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                return new AppSettings();

            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(settings, JsonOptions);

            var tempPath = _filePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, _filePath, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }
}
