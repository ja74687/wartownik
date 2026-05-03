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

    private readonly UpdateManager _manager;

    // Velopack's own UpdateInfo lives keyed against our wrapper's so we can map back
    // when ApplyAndRestart is called without leaking the Velopack types upstream.
    private readonly Dictionary<string, Velopack.UpdateInfo> _pending = new(StringComparer.Ordinal);

    public VelopackUpdateService()
    {
        _manager = new UpdateManager(new GithubSource(GitHubRepoUrl, accessToken: null, prerelease: false));
    }

    public bool IsInstalled => _manager.IsInstalled;

    public async Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (!_manager.IsInstalled)
            return null;

        var info = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        if (info is null)
            return null;

        var version = info.TargetFullRelease.Version.ToString();
        _pending[version] = info;
        return new UpdateInfo(version);
    }

    public async Task DownloadAsync(UpdateInfo update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (!_pending.TryGetValue(update.TargetVersion, out var info))
            return;
        await _manager.DownloadUpdatesAsync(info).ConfigureAwait(false);
    }

    public void ApplyAndRestart(UpdateInfo update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (!_pending.TryGetValue(update.TargetVersion, out var info))
            return;
        _manager.ApplyUpdatesAndRestart(info);
    }
}
