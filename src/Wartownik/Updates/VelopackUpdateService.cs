using Velopack;
using Velopack.Sources;

namespace Wartownik.Updates;

/// <summary>
/// Velopack-backed implementation. Uses GitHub Releases as the update source — no
/// dedicated server, no infra cost. Repo URL is hard-coded; change here if forking.
/// </summary>
public sealed class VelopackUpdateService : IUpdateService
{
    private const string GitHubRepoUrl = "https://github.com/ja74687/wartownik";

    private UpdateManager? _manager;
    private bool _managerResolved;

    // Velopack's own UpdateInfo lives keyed against our wrapper's so we can map back
    // when ApplyAndRestart is called without leaking the Velopack types upstream.
    private readonly Dictionary<string, Velopack.UpdateInfo> _pending = new(StringComparer.Ordinal);

    /// <summary>
    /// The UpdateManager, or null when this process isn't an installed Velopack build.
    /// Built lazily and defensively: constructing it requires VelopackApp.Build().Run() to have
    /// run first, which is true in the shipped app but not under `dotnet run`, the designer, or
    /// unit tests. Those cases degrade to "no updates available" instead of throwing.
    /// </summary>
    private UpdateManager? Manager
    {
        get
        {
            if (_managerResolved)
                return _manager;

            _managerResolved = true;
            try
            {
                _manager = new UpdateManager(new GithubSource(GitHubRepoUrl, accessToken: null, prerelease: false));
            }
            catch
            {
                _manager = null;
            }

            return _manager;
        }
    }

    public bool IsInstalled => Manager?.IsInstalled ?? false;

    public async Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (Manager is not { IsInstalled: true } manager)
            return null;

        var info = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
        if (info is null)
            return null;

        var version = info.TargetFullRelease.Version.ToString();
        _pending[version] = info;
        return new UpdateInfo(version);
    }

    public async Task DownloadAsync(UpdateInfo update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (Manager is not { } manager)
            return;
        if (!_pending.TryGetValue(update.TargetVersion, out var info))
            return;
        await manager.DownloadUpdatesAsync(info).ConfigureAwait(false);
    }

    public void ApplyAndRestart(UpdateInfo update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (Manager is not { } manager)
            return;
        if (!_pending.TryGetValue(update.TargetVersion, out var info))
            return;
        manager.ApplyUpdatesAndRestart(info);
    }
}
