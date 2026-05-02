using Npgsql;
using Wartownik.Connections;

namespace Wartownik.Postgres;

public sealed class NpgsqlConnectionStringFactory
{
    public const string ApplicationName = "Wartownik";

    public string Build(ConnectionProfile profile, string password)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(password);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = profile.Host,
            Port = profile.Port,
            Database = profile.Database,
            Username = profile.Username,
            Password = password,
            ApplicationName = ApplicationName,
            SslMode = MapSslMode(profile.SslMode),
        };

        return builder.ToString();
    }

    private static SslMode MapSslMode(PostgresSslMode mode) => mode switch
    {
        PostgresSslMode.Disable => SslMode.Disable,
        PostgresSslMode.Allow => SslMode.Allow,
        PostgresSslMode.Prefer => SslMode.Prefer,
        PostgresSslMode.Require => SslMode.Require,
        PostgresSslMode.VerifyCa => SslMode.VerifyCA,
        PostgresSslMode.VerifyFull => SslMode.VerifyFull,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown SSL mode."),
    };
}
