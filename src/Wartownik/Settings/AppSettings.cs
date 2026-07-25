namespace Wartownik.Settings;

/// <summary>
/// Persisted application-wide preferences (as opposed to per-connection settings, which live on
/// <see cref="Connections.ConnectionProfile"/>). Serialized to settings.json in the app data
/// directory. Kept as an immutable record so callers update via <c>with</c> and nothing mutates
/// a shared instance behind their back. New preferences are added as extra init properties;
/// missing properties in an older file simply fall back to their defaults on load.
/// </summary>
public sealed record AppSettings
{
    /// <summary>
    /// Culture name of the chosen UI language (e.g. "en", "pl"), or null when the user has never
    /// picked one — in which case the app falls back to its default language.
    /// </summary>
    public string? Language { get; init; }
}
