namespace Wartownik.Connections.Credentials;

public sealed class InMemoryCredentialStore : ICredentialStore
{
    private readonly Dictionary<string, string> _entries = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    public string? Get(string key)
    {
        ValidateKey(key);
        lock (_lock)
        {
            return _entries.TryGetValue(key, out var secret) ? secret : null;
        }
    }

    public void Set(string key, string secret)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(secret);
        lock (_lock)
        {
            _entries[key] = secret;
        }
    }

    public bool Remove(string key)
    {
        ValidateKey(key);
        lock (_lock)
        {
            return _entries.Remove(key);
        }
    }

    internal static void ValidateKey(string key) =>
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
}
