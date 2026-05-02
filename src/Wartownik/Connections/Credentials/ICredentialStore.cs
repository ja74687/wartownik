namespace Wartownik.Connections.Credentials;

public interface ICredentialStore
{
    string? Get(string key);

    void Set(string key, string secret);

    bool Remove(string key);
}
