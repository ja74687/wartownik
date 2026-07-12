using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wartownik.Connections;

/// <summary>
/// Import/export codec for sharing connection profiles as JSON. Deliberately omits:
/// the internal Id (a fresh one is minted on import so importing never overwrites an
/// existing profile), the password (that lives in the OS keystore, never on disk in
/// plaintext — the user adds it after importing), and local-only metadata like LastEditedAt.
/// </summary>
public static class ConnectionProfileJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(ConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return JsonSerializer.Serialize(
            new Dto
            {
                DisplayName = profile.DisplayName,
                Host = profile.Host,
                Port = profile.Port,
                Database = profile.Database,
                Username = profile.Username,
                SslMode = profile.SslMode,
            },
            Options);
    }

    /// <summary>
    /// Parse one or more profiles from a JSON document that is either a single profile object
    /// or an array of them. Every parsed profile gets a fresh Id; a missing SSL mode defaults
    /// to Require (TLS by default) and a missing port to 5432. Returns false with a message
    /// when the JSON is malformed or a required field (display name, host, database, username)
    /// is blank.
    /// </summary>
    public static bool TryParse(string json, out IReadOnlyList<ConnectionProfile> profiles, out string? error)
    {
        profiles = Array.Empty<ConnectionProfile>();
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "The file is empty.";
            return false;
        }

        List<Dto?>? dtos;
        try
        {
            dtos = json.TrimStart().StartsWith('[')
                ? JsonSerializer.Deserialize<List<Dto?>>(json, Options)
                : new List<Dto?> { JsonSerializer.Deserialize<Dto>(json, Options) };
        }
        catch (JsonException ex)
        {
            error = $"Not valid JSON: {ex.Message}";
            return false;
        }

        if (dtos is null || dtos.Count == 0)
        {
            error = "No profiles found in the file.";
            return false;
        }

        var result = new List<ConnectionProfile>(dtos.Count);
        foreach (var dto in dtos)
        {
            if (dto is null)
            {
                error = "The file contains an empty profile entry.";
                return false;
            }
            try
            {
                result.Add(ConnectionProfile.Create(
                    dto.DisplayName ?? "",
                    dto.Host ?? "",
                    dto.Port ?? ConnectionProfile.DefaultPort,
                    dto.Database ?? "",
                    dto.Username ?? "",
                    dto.SslMode ?? PostgresSslMode.Require));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
            {
                error = $"Invalid profile \"{dto.DisplayName}\": {ex.Message}";
                return false;
            }
        }

        profiles = result;
        return true;
    }

    private sealed class Dto
    {
        public string? DisplayName { get; set; }
        public string? Host { get; set; }
        public int? Port { get; set; }
        public string? Database { get; set; }
        public string? Username { get; set; }
        public PostgresSslMode? SslMode { get; set; }
    }
}
