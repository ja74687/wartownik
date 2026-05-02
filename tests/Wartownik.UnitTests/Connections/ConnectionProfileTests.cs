using Wartownik.Connections;

namespace Wartownik.UnitTests.Connections;

public class ConnectionProfileTests
{
    [Fact]
    public void Create_with_valid_data_returns_profile_with_new_id()
    {
        var profile = ConnectionProfile.Create("local", "localhost", 5432, "mydb", "alice");

        Assert.NotEqual(Guid.Empty, profile.Id);
        Assert.Equal("local", profile.DisplayName);
        Assert.Equal("localhost", profile.Host);
        Assert.Equal(5432, profile.Port);
        Assert.Equal("mydb", profile.Database);
        Assert.Equal("alice", profile.Username);
        Assert.Equal(PostgresSslMode.Require, profile.SslMode);
    }

    [Fact]
    public void Create_uses_explicit_id_when_provided()
    {
        var id = Guid.NewGuid();
        var profile = ConnectionProfile.Create(
            id, "local", "localhost", 5432, "mydb", "alice", PostgresSslMode.Disable);

        Assert.Equal(id, profile.Id);
        Assert.Equal(PostgresSslMode.Disable, profile.SslMode);
    }

    [Fact]
    public void Create_trims_string_inputs()
    {
        var profile = ConnectionProfile.Create("  local  ", "  localhost  ", 5432, "  mydb  ", "  alice  ");

        Assert.Equal("local", profile.DisplayName);
        Assert.Equal("localhost", profile.Host);
        Assert.Equal("mydb", profile.Database);
        Assert.Equal("alice", profile.Username);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_blank_display_name(string? displayName)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            ConnectionProfile.Create(displayName!, "host", 5432, "db", "user"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_blank_host(string? host)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            ConnectionProfile.Create("name", host!, 5432, "db", "user"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_blank_database(string? database)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            ConnectionProfile.Create("name", "host", 5432, database!, "user"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_blank_username(string? username)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            ConnectionProfile.Create("name", "host", 5432, "db", username!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    [InlineData(int.MaxValue)]
    public void Create_rejects_port_out_of_range(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ConnectionProfile.Create("name", "host", port, "db", "user"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5432)]
    [InlineData(65535)]
    public void Create_accepts_port_at_boundaries(int port)
    {
        var profile = ConnectionProfile.Create("name", "host", port, "db", "user");
        Assert.Equal(port, profile.Port);
    }

    [Fact]
    public void Create_rejects_display_name_above_max_length()
    {
        var tooLong = new string('a', ConnectionProfile.MaxDisplayNameLength + 1);
        Assert.Throws<ArgumentException>(() =>
            ConnectionProfile.Create(tooLong, "host", 5432, "db", "user"));
    }

    [Fact]
    public void Create_accepts_display_name_at_max_length()
    {
        var maxLen = new string('a', ConnectionProfile.MaxDisplayNameLength);
        var profile = ConnectionProfile.Create(maxLen, "host", 5432, "db", "user");
        Assert.Equal(maxLen, profile.DisplayName);
    }

    [Fact]
    public void Create_rejects_invalid_ssl_mode()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ConnectionProfile.Create(
                Guid.NewGuid(), "name", "host", 5432, "db", "user", (PostgresSslMode)999));
    }

    [Fact]
    public void Create_rejects_empty_guid()
    {
        Assert.Throws<ArgumentException>(() =>
            ConnectionProfile.Create(
                Guid.Empty, "name", "host", 5432, "db", "user", PostgresSslMode.Require));
    }

    [Fact]
    public void Default_ssl_mode_is_require()
    {
        var profile = ConnectionProfile.Create("name", "host", 5432, "db", "user");
        Assert.Equal(PostgresSslMode.Require, profile.SslMode);
    }
}
