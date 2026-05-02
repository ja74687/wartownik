using Npgsql;
using Wartownik.Connections;
using Wartownik.Postgres;

namespace Wartownik.UnitTests.Postgres;

public class NpgsqlConnectionStringFactoryTests
{
    private readonly NpgsqlConnectionStringFactory _factory = new();

    private static ConnectionProfile SampleProfile(PostgresSslMode sslMode = PostgresSslMode.Require) =>
        ConnectionProfile.Create(
            displayName: "Local",
            host: "db.example.com",
            port: 5433,
            database: "mydb",
            username: "alice",
            sslMode: sslMode);

    [Fact]
    public void Build_maps_core_fields()
    {
        var connectionString = _factory.Build(SampleProfile(), "secret");
        var parsed = new NpgsqlConnectionStringBuilder(connectionString);

        Assert.Equal("db.example.com", parsed.Host);
        Assert.Equal(5433, parsed.Port);
        Assert.Equal("mydb", parsed.Database);
        Assert.Equal("alice", parsed.Username);
        Assert.Equal("secret", parsed.Password);
    }

    [Fact]
    public void Build_sets_application_name_to_Wartownik()
    {
        var connectionString = _factory.Build(SampleProfile(), "secret");
        var parsed = new NpgsqlConnectionStringBuilder(connectionString);

        Assert.Equal(NpgsqlConnectionStringFactory.ApplicationName, parsed.ApplicationName);
        Assert.Equal("Wartownik", parsed.ApplicationName);
    }

    [Theory]
    [InlineData(PostgresSslMode.Disable, SslMode.Disable)]
    [InlineData(PostgresSslMode.Allow, SslMode.Allow)]
    [InlineData(PostgresSslMode.Prefer, SslMode.Prefer)]
    [InlineData(PostgresSslMode.Require, SslMode.Require)]
    [InlineData(PostgresSslMode.VerifyCa, SslMode.VerifyCA)]
    [InlineData(PostgresSslMode.VerifyFull, SslMode.VerifyFull)]
    public void Build_maps_ssl_mode(PostgresSslMode profileMode, SslMode expected)
    {
        var connectionString = _factory.Build(SampleProfile(profileMode), "secret");
        var parsed = new NpgsqlConnectionStringBuilder(connectionString);

        Assert.Equal(expected, parsed.SslMode);
    }

    [Fact]
    public void Build_escapes_password_with_special_characters()
    {
        // Includes ;, ', " and a space to validate proper escaping by the builder.
        const string trickyPassword = "p;a's\"s w";
        var connectionString = _factory.Build(SampleProfile(), trickyPassword);
        var parsed = new NpgsqlConnectionStringBuilder(connectionString);

        Assert.Equal(trickyPassword, parsed.Password);
    }

    [Fact]
    public void Build_throws_on_null_profile()
    {
        Assert.Throws<ArgumentNullException>(() => _factory.Build(null!, "secret"));
    }

    [Fact]
    public void Build_throws_on_null_password()
    {
        Assert.Throws<ArgumentNullException>(() => _factory.Build(SampleProfile(), null!));
    }
}
