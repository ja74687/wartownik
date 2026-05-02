using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Wartownik.Connections.Credentials;

[SupportedOSPlatform("windows")]
public sealed class WindowsDpapiCredentialStore : ICredentialStore
{
    private readonly string _filePath;
    private readonly byte[] _entropy;
    private readonly Lock _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public WindowsDpapiCredentialStore(string serviceName)
        : this(serviceName, DefaultPathFor(serviceName))
    {
    }

    public WindowsDpapiCredentialStore(string serviceName, string filePath)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DPAPI is only available on Windows.");
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        _filePath = filePath;
        _entropy = SHA256.HashData(Encoding.UTF8.GetBytes("Wartownik:" + serviceName));
    }

    public string? Get(string key)
    {
        InMemoryCredentialStore.ValidateKey(key);
        lock (_lock)
        {
            var entries = LoadEntries();
            if (!entries.TryGetValue(key, out var encryptedBase64))
                return null;

            var encrypted = Convert.FromBase64String(encryptedBase64);
            var plain = ProtectedData.Unprotect(encrypted, _entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
    }

    public void Set(string key, string secret)
    {
        InMemoryCredentialStore.ValidateKey(key);
        ArgumentNullException.ThrowIfNull(secret);
        lock (_lock)
        {
            var entries = LoadEntries();
            var protectedBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(secret), _entropy, DataProtectionScope.CurrentUser);
            entries[key] = Convert.ToBase64String(protectedBytes);
            SaveEntries(entries);
        }
    }

    public bool Remove(string key)
    {
        InMemoryCredentialStore.ValidateKey(key);
        lock (_lock)
        {
            var entries = LoadEntries();
            if (!entries.Remove(key))
                return false;
            SaveEntries(entries);
            return true;
        }
    }

    private Dictionary<string, string> LoadEntries()
    {
        if (!File.Exists(_filePath))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        var json = File.ReadAllText(_filePath);
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        var doc = JsonSerializer.Deserialize<CredentialFile>(json, JsonOptions);
        return doc?.Entries ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private void SaveEntries(Dictionary<string, string> entries)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var doc = new CredentialFile { Version = 1, Entries = entries };
        var json = JsonSerializer.Serialize(doc, JsonOptions);

        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _filePath, overwrite: true);
    }

    private static string DefaultPathFor(string serviceName)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, serviceName, "credentials.json");
    }

    private sealed class CredentialFile
    {
        public int Version { get; set; }
        public Dictionary<string, string>? Entries { get; set; }
    }
}
