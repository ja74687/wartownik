namespace Wartownik.Settings;

/// <summary>
/// Loads and saves the application-wide <see cref="AppSettings"/>. Loading a store that was never
/// written returns a default <see cref="AppSettings"/> rather than throwing, so first-run has no
/// special case.
/// </summary>
public interface IAppSettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
