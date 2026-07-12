using Wartownik.Connections;

namespace Wartownik.UnitTests.Connections;

public class ConnectionProfileJsonTests
{
    private static ConnectionProfile Sample() =>
        ConnectionProfile.Create("Local dev", "192.168.88.10", 5432, "postgres", "admin", PostgresSslMode.VerifyFull);

    [Fact]
    public void Serialize_then_TryParse_round_trips_the_shared_fields()
    {
        var original = Sample();

        var json = ConnectionProfileJson.Serialize(original);
        Assert.True(ConnectionProfileJson.TryParse(json, out var parsed, out var error));

        Assert.Null(error);
        var loaded = Assert.Single(parsed);
        Assert.Equal(original.DisplayName, loaded.DisplayName);
        Assert.Equal(original.Host, loaded.Host);
        Assert.Equal(original.Port, loaded.Port);
        Assert.Equal(original.Database, loaded.Database);
        Assert.Equal(original.Username, loaded.Username);
        Assert.Equal(original.SslMode, loaded.SslMode);
    }

    [Fact]
    public void Serialize_omits_id_and_password()
    {
        var json = ConnectionProfileJson.Serialize(Sample());

        Assert.DoesNotContain("\"id\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        // SSL mode is written as a readable name, not a bare number.
        Assert.Contains("VerifyFull", json);
    }

    [Fact]
    public void TryParse_mints_a_fresh_id_so_import_never_overwrites()
    {
        var original = Sample();
        var json = ConnectionProfileJson.Serialize(original);

        Assert.True(ConnectionProfileJson.TryParse(json, out var parsed, out _));

        Assert.NotEqual(original.Id, parsed[0].Id);
        Assert.NotEqual(Guid.Empty, parsed[0].Id);
    }

    [Fact]
    public void TryParse_accepts_a_json_array_of_profiles()
    {
        const string json = """
            [
              { "displayName": "dev",  "host": "localhost", "port": 5432, "database": "d1", "username": "u1", "sslMode": "Disable" },
              { "displayName": "prod", "host": "db.example.com", "port": 5433, "database": "d2", "username": "u2", "sslMode": "Require" }
            ]
            """;

        Assert.True(ConnectionProfileJson.TryParse(json, out var parsed, out _));
        Assert.Equal(2, parsed.Count);
        Assert.Equal(new[] { "dev", "prod" }, parsed.Select(p => p.DisplayName));
    }

    [Fact]
    public void TryParse_missing_sslMode_defaults_to_Require()
    {
        const string json = """
            { "displayName": "dev", "host": "localhost", "port": 5432, "database": "d1", "username": "u1" }
            """;

        Assert.True(ConnectionProfileJson.TryParse(json, out var parsed, out _));
        Assert.Equal(PostgresSslMode.Require, parsed[0].SslMode);
    }

    [Fact]
    public void TryParse_missing_port_defaults_to_5432()
    {
        const string json = """
            { "displayName": "dev", "host": "localhost", "database": "d1", "username": "u1", "sslMode": "Require" }
            """;

        Assert.True(ConnectionProfileJson.TryParse(json, out var parsed, out _));
        Assert.Equal(5432, parsed[0].Port);
    }

    [Fact]
    public void TryParse_blank_input_fails_gracefully()
    {
        Assert.False(ConnectionProfileJson.TryParse("   ", out var parsed, out var error));
        Assert.Empty(parsed);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParse_malformed_json_fails_with_message()
    {
        Assert.False(ConnectionProfileJson.TryParse("{ not json", out _, out var error));
        Assert.NotNull(error);
        Assert.Contains("valid JSON", error);
    }

    [Fact]
    public void TryParse_missing_required_field_fails_with_message()
    {
        // Blank host — ConnectionProfile.Create rejects it.
        const string json = """
            { "displayName": "dev", "host": "", "port": 5432, "database": "d1", "username": "u1" }
            """;

        Assert.False(ConnectionProfileJson.TryParse(json, out _, out var error));
        Assert.NotNull(error);
    }
}
