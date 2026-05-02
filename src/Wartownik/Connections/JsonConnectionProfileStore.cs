using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wartownik.Connections;

public sealed class JsonConnectionProfileStore : IConnectionProfileStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public JsonConnectionProfileStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    public async Task<IReadOnlyList<ConnectionProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadAllAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ConnectionProfile?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var all = await ReadAllAsync(cancellationToken).ConfigureAwait(false);
            return all.FirstOrDefault(p => p.Id == id);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var all = (await ReadAllAsync(cancellationToken).ConfigureAwait(false)).ToList();
            var index = all.FindIndex(p => p.Id == profile.Id);
            if (index >= 0)
                all[index] = profile;
            else
                all.Add(profile);
            await WriteAllAsync(all, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var all = (await ReadAllAsync(cancellationToken).ConfigureAwait(false)).ToList();
            var removed = all.RemoveAll(p => p.Id == id);
            if (removed == 0)
                return false;
            await WriteAllAsync(all, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<ConnectionProfile>> ReadAllAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
            return [];

        var json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
            return [];

        var doc = JsonSerializer.Deserialize<ProfileFile>(json, JsonOptions);
        if (doc?.Profiles is null)
            return [];

        return doc.Profiles.Select(p => p.ToDomain()).ToList();
    }

    private async Task WriteAllAsync(List<ConnectionProfile> profiles, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var doc = new ProfileFile
        {
            Profiles = profiles.Select(ConnectionProfileDto.FromDomain).ToList(),
        };
        var json = JsonSerializer.Serialize(doc, JsonOptions);

        var tempPath = _filePath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, _filePath, overwrite: true);
    }

    private sealed class ProfileFile
    {
        public List<ConnectionProfileDto>? Profiles { get; set; }
    }

    private sealed class ConnectionProfileDto
    {
        public Guid Id { get; set; }
        public string DisplayName { get; set; } = "";
        public string Host { get; set; } = "";
        public int Port { get; set; }
        public string Database { get; set; } = "";
        public string Username { get; set; } = "";
        public PostgresSslMode SslMode { get; set; }

        public ConnectionProfile ToDomain() =>
            ConnectionProfile.Create(Id, DisplayName, Host, Port, Database, Username, SslMode);

        public static ConnectionProfileDto FromDomain(ConnectionProfile profile) => new()
        {
            Id = profile.Id,
            DisplayName = profile.DisplayName,
            Host = profile.Host,
            Port = profile.Port,
            Database = profile.Database,
            Username = profile.Username,
            SslMode = profile.SslMode,
        };
    }
}
