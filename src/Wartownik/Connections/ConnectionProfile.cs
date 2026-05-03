namespace Wartownik.Connections;

public sealed record ConnectionProfile
{
    public required Guid Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string Database { get; init; }
    public required string Username { get; init; }
    public required PostgresSslMode SslMode { get; init; }

    /// <summary>
    /// Last time this profile was created or edited. Optional because we want to keep
    /// reading older profiles.json files that don't have this field — they materialise as null
    /// and surface as "—" in the UI rather than a wrong/zero timestamp.
    /// </summary>
    public DateTimeOffset? LastEditedAt { get; init; }

    public const int DefaultPort = 5432;
    public const int MaxDisplayNameLength = 100;

    public static ConnectionProfile Create(
        string displayName,
        string host,
        int port,
        string database,
        string username,
        PostgresSslMode sslMode = PostgresSslMode.Require)
        => Create(Guid.NewGuid(), displayName, host, port, database, username, sslMode);

    public static ConnectionProfile Create(
        Guid id,
        string displayName,
        string host,
        int port,
        string database,
        string username,
        PostgresSslMode sslMode)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Id must not be empty.", nameof(id));

        var trimmedDisplayName = RequireNonBlank(displayName, nameof(displayName));
        if (trimmedDisplayName.Length > MaxDisplayNameLength)
            throw new ArgumentException(
                $"DisplayName must not exceed {MaxDisplayNameLength} characters.", nameof(displayName));

        var trimmedHost = RequireNonBlank(host, nameof(host));
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be in range 1..65535.");

        var trimmedDatabase = RequireNonBlank(database, nameof(database));
        var trimmedUsername = RequireNonBlank(username, nameof(username));

        if (!Enum.IsDefined(sslMode))
            throw new ArgumentOutOfRangeException(nameof(sslMode), sslMode, "Unknown SSL mode.");

        return new ConnectionProfile
        {
            Id = id,
            DisplayName = trimmedDisplayName,
            Host = trimmedHost,
            Port = port,
            Database = trimmedDatabase,
            Username = trimmedUsername,
            SslMode = sslMode,
            LastEditedAt = null,
        };
    }

    private static string RequireNonBlank(string value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
            throw new ArgumentException($"{paramName} must not be blank.", paramName);
        return trimmed;
    }
}
