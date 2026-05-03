namespace Wartownik.Updates;

/// <summary>
/// Thin wrapper around Velopack's UpdateManager so the rest of the app doesn't take
/// a hard dependency on the updater. In dev runs (no installed channel) every method
/// is a no-op, so calling code doesn't need to special-case it.
/// </summary>
public interface IUpdateService
{
    /// <summary>True when the app was launched from a Velopack-installed location
    /// (i.e. there is a channel to check against). False under "dotnet run" / IDE.</summary>
    bool IsInstalled { get; }

    /// <summary>Hits the release feed and returns the available update if any, otherwise null.</summary>
    Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default);

    /// <summary>Downloads the update bytes locally; safe to call before the user confirms.</summary>
    Task DownloadAsync(UpdateInfo update, CancellationToken cancellationToken = default);

    /// <summary>Applies the previously-downloaded update and restarts the app.</summary>
    void ApplyAndRestart(UpdateInfo update);
}

public sealed record UpdateInfo(string TargetVersion);
