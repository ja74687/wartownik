using System.Collections.ObjectModel;
using Wartownik.Connections;
using Wartownik.Localization;

namespace Wartownik.ViewModels;

/// <summary>
/// One row in the sticky bar's "pending per role" list. Lets the user see at a glance
/// who has staged edits and how many of each kind, even when they're scattered across
/// several users in the dropdown.
/// </summary>
public sealed record PendingGroup(string RoleName, int TotalCount, int GrantCount, int RevokeCount)
{
    public string Summary => $"+{GrantCount} / -{RevokeCount}";
}

/// <summary>
/// Permissions matrix for one (database, target role) pair.
///
/// Lifecycle:
/// 1. LoadRolesAsync — fetch all roles in the cluster, populate TargetRoles, pick a default.
/// 2. When SelectedRole changes (or LoadGrantsAsync is called) — fetch the role's effective
///    schema-level grants and populate Rows.
/// 3. User toggles checkboxes; PendingChanges reflects the staged diff.
/// 4. ApplyAsync — submit the diff to PostgresGrantService transactionally, then refresh.
/// </summary>
public sealed class PermissionsMatrixViewModel : ViewModelBase
{
    private readonly IConnectionProfileService _profiles;
    private readonly IPostgresMetadataService _metadata;
    private readonly IPostgresGrantService _grants;

    private RoleSummary? _selectedRole;
    private bool _isLoading;
    private bool _isApplying;
    private string? _errorMessage;
    private string? _statusMessage;

    /// <summary>
    /// Pending edits keyed by role name and (schema, privilege). Lets the user iterate between
    /// users in the dropdown without losing what they've staged on each one. Cleared per role
    /// after a successful Apply or Discard.
    /// </summary>
    private readonly Dictionary<string, Dictionary<(string Schema, GrantPrivilege Privilege), bool>> _pendingByRole =
        new(StringComparer.Ordinal);

    public ConnectionProfile Profile { get; }
    public string DatabaseName { get; }
    public ILocalizationService Localization { get; }

    public ObservableCollection<RoleSummary> TargetRoles { get; } = new();
    public ObservableCollection<SchemaPermissionRowViewModel> Rows { get; } = new();

    public AsyncRelayCommand LoadCommand { get; }
    public AsyncRelayCommand ApplyCommand { get; }
    public RelayCommand DiscardCommand { get; }
    public AsyncRelayCommand ApplyRoleCommand { get; }
    public RelayCommand DiscardRoleCommand { get; }

    public PermissionsMatrixViewModel(
        ConnectionProfile profile,
        string databaseName,
        ILocalizationService localization,
        IConnectionProfileService profiles,
        IPostgresMetadataService metadata,
        IPostgresGrantService grants)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(grants);

        Profile = profile;
        DatabaseName = databaseName;
        Localization = localization;
        _profiles = profiles;
        _metadata = metadata;
        _grants = grants;

        LoadCommand = new AsyncRelayCommand(() => LoadAsync());
        ApplyCommand = new AsyncRelayCommand(() => ApplyAsync(), () => HasPending && !_isApplying);
        DiscardCommand = new RelayCommand(Discard, () => HasPending && !_isApplying);
        ApplyRoleCommand = new AsyncRelayCommand(p => ApplyRoleAsync(p as string));
        DiscardRoleCommand = new RelayCommand(p => DiscardRole(p as string));
    }

    public RoleSummary? SelectedRole
    {
        get => _selectedRole;
        set
        {
            if (ReferenceEquals(_selectedRole, value))
                return;

            // Stash the outgoing role's pending edits BEFORE we flip — once SelectedRole changes
            // the Rows collection will be refreshed for the new role and the in-flight diff is
            // gone unless we capture it here.
            CapturePendingForRole(_selectedRole);

            SetField(ref _selectedRole, value);
            RaisePropertyChanged(nameof(HasSelectedRole));

            // Fire and forget the grants refresh — UI shows IsLoading while it runs.
            _ = LoadGrantsForSelectedRoleAsync();
        }
    }

    /// <summary>
    /// Snapshot the dirty cells under <paramref name="role"/>'s name. Empty pending lists
    /// drop the entry so the dictionary doesn't grow with users who have nothing staged.
    /// </summary>
    private void CapturePendingForRole(RoleSummary? role)
    {
        if (role is null)
            return;
        var dirty = Rows
            .SelectMany(r => r.Cells.Where(c => c.IsDirty)
                .Select(c => ((Schema: r.SchemaName, Privilege: c.Privilege), c.PendingValue)))
            .ToDictionary(t => t.Item1, t => t.PendingValue);
        if (dirty.Count == 0)
            _pendingByRole.Remove(role.Name);
        else
            _pendingByRole[role.Name] = dirty;
        // Cache changed — totals shown in the sticky bar need to refresh.
        RaisePendingAggregates();
    }

    /// <summary>
    /// After Rows are populated for the freshly-selected role, replay any cached pending edits
    /// the user had staged for them previously.
    /// </summary>
    private void RestorePendingForCurrentRole()
    {
        if (_selectedRole is null)
            return;
        if (!_pendingByRole.TryGetValue(_selectedRole.Name, out var cached))
            return;
        foreach (var row in Rows)
        {
            foreach (var cell in row.Cells)
            {
                if (cached.TryGetValue((row.SchemaName, cell.Privilege), out var desired)
                    && cell.PendingValue != desired)
                {
                    cell.Toggle();
                }
            }
        }
    }

    public bool HasSelectedRole => _selectedRole is not null;

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetField(ref _isLoading, value))
                RaisePropertyChanged(nameof(IsContentVisible));
        }
    }

    public bool IsApplying
    {
        get => _isApplying;
        private set
        {
            if (SetField(ref _isApplying, value))
            {
                ApplyCommand.RaiseCanExecuteChanged();
                DiscardCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value))
                RaisePropertyChanged(nameof(HasError));
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetField(ref _statusMessage, value))
                RaisePropertyChanged(nameof(HasStatus));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_errorMessage);
    public bool HasStatus => !string.IsNullOrEmpty(_statusMessage);

    public bool IsContentVisible => !IsLoading && !HasError && HasSelectedRole;

    /// <summary>Dirty cells in the rows currently displayed (i.e. for SelectedRole).</summary>
    public int CurrentRowsPendingCount => Rows.Sum(r => r.PendingCount);

    /// <summary>
    /// Total pending edits across every role the user has touched in this session
    /// (the dropdown caches dirty state per role — those counts are summed here too).
    /// This is what the sticky bottom bar shows so the user never loses sight of
    /// staged edits on other roles.
    /// </summary>
    public int PendingCount => CurrentRowsPendingCount + _pendingByRole.Values.Sum(d => d.Count);

    public bool HasPending => PendingCount > 0;

    /// <summary>Number of unique roles with pending edits — current row plus cached.</summary>
    public int PendingRoleCount =>
        (CurrentRowsPendingCount > 0 ? 1 : 0) + _pendingByRole.Count;

    public bool HasMultiplePendingRoles => PendingRoleCount > 1;

    public int PendingGrantCount =>
        Rows.Sum(r => r.Cells.Count(c => c.State == CellState.PendingGrant))
        + _pendingByRole.Sum(kvp => kvp.Value.Count(p => p.Value)); // cached pending=true means grant

    public int PendingRevokeCount =>
        Rows.Sum(r => r.Cells.Count(c => c.State == CellState.PendingRevoke))
        + _pendingByRole.Sum(kvp => kvp.Value.Count(p => !p.Value)); // cached pending=false means revoke

    /// <summary>
    /// Pending edits grouped by target role. Includes both the visible rows (under SelectedRole)
    /// and any cached edits for other roles in the dropdown. Sorted by role name for stability.
    /// </summary>
    public IReadOnlyList<PendingGroup> PendingGroups
    {
        get
        {
            var groups = new Dictionary<string, (int total, int grants, int revokes)>(StringComparer.Ordinal);

            // Cached entries — stash for other roles.
            foreach (var (roleName, diff) in _pendingByRole)
            {
                var grants = diff.Count(p => p.Value);
                var revokes = diff.Count - grants;
                groups[roleName] = (diff.Count, grants, revokes);
            }

            // Visible rows — current role's live in-flight edits.
            if (_selectedRole is not null)
            {
                var liveGrants = Rows.Sum(r => r.Cells.Count(c => c.State == CellState.PendingGrant));
                var liveRevokes = Rows.Sum(r => r.Cells.Count(c => c.State == CellState.PendingRevoke));
                var total = liveGrants + liveRevokes;
                if (total > 0)
                    groups[_selectedRole.Name] = (total, liveGrants, liveRevokes);
            }

            return groups
                .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kvp => new PendingGroup(kvp.Key, kvp.Value.total, kvp.Value.grants, kvp.Value.revokes))
                .ToList();
        }
    }

    /// <summary>
    /// Flat list of GrantChange records reflecting every dirty cell. Used by Preview SQL
    /// and Apply to know what the user wants to do.
    /// </summary>
    public IReadOnlyList<GrantChange> CollectPendingChanges() =>
        Rows.SelectMany(r => r.EnumeratePendingChanges()).ToList();

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            var password = await _profiles
                .GetPasswordAsync(Profile.Id, cancellationToken)
                .ConfigureAwait(true) ?? "";

            var roles = await _metadata
                .ListRolesAsync(Profile, password, cancellationToken)
                .ConfigureAwait(true);

            // Keep the dropdown focused on actionable targets: roles that can log in are users
            // you'd actually grant table-level privileges to. Group roles can still be granted
            // via membership but that's a future iteration.
            var actionable = roles.Where(r => r.CanLogin).ToList();

            TargetRoles.Clear();
            foreach (var r in actionable)
                TargetRoles.Add(r);

            // Preserve previous selection if still present, else fall back to the first row.
            var keepName = _selectedRole?.Name;
            var next = actionable.FirstOrDefault(r => r.Name == keepName) ?? actionable.FirstOrDefault();
            if (!ReferenceEquals(next, _selectedRole))
                SelectedRole = next; // setter triggers grants load
            else if (next is not null)
                await LoadGrantsForSelectedRoleAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task LoadGrantsForSelectedRoleAsync(CancellationToken cancellationToken = default)
    {
        if (_selectedRole is null)
        {
            Rows.Clear();
            RaisePendingAggregates();
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var password = await _profiles
                .GetPasswordAsync(Profile.Id, cancellationToken)
                .ConfigureAwait(true) ?? "";

            var summaries = await _grants
                .ListSchemaGrantsAsync(Profile, password, DatabaseName, _selectedRole.Name, cancellationToken)
                .ConfigureAwait(true);

            Rows.Clear();
            foreach (var summary in summaries)
                Rows.Add(new SchemaPermissionRowViewModel(summary, RaisePendingAggregates));

            // Reapply any edits the user had staged for this role earlier in the session.
            RestorePendingForCurrentRole();
            RaisePendingAggregates();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Throw away every pending edit — across all roles the user has touched.
    /// The sticky bar shows totals across roles, so Discard's blast radius matches.
    /// </summary>
    public void Discard()
    {
        foreach (var row in Rows)
            row.DiscardPending();
        _pendingByRole.Clear();
        StatusMessage = null;
        RaisePendingAggregates();
    }

    /// <summary>
    /// Drop pending edits for a single role. If it's the currently visible role we revert
    /// the live cells; otherwise we just forget the cached diff for that name.
    /// </summary>
    public void DiscardRole(string? roleName)
    {
        if (string.IsNullOrEmpty(roleName))
            return;

        if (_selectedRole?.Name == roleName)
        {
            foreach (var row in Rows)
                row.DiscardPending();
        }
        _pendingByRole.Remove(roleName);
        RaisePendingAggregates();
    }

    /// <summary>
    /// Apply pending edits for a single role only. Useful when the user has staged work for
    /// several users and wants to ship them one at a time. Other roles' edits stay in cache.
    /// </summary>
    public async Task ApplyRoleAsync(string? roleName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(roleName))
            return;

        // Make sure the in-flight visible diff is captured before we read the cache.
        if (_selectedRole?.Name == roleName)
            CapturePendingForRole(_selectedRole);

        if (!_pendingByRole.TryGetValue(roleName, out var diff) || diff.Count == 0)
            return;

        IsApplying = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            var password = await _profiles
                .GetPasswordAsync(Profile.Id, cancellationToken)
                .ConfigureAwait(true) ?? "";

            var changes = diff
                .Select(kvp => new GrantChange(
                    kvp.Key.Schema,
                    kvp.Key.Privilege,
                    kvp.Value ? GrantOperation.Grant : GrantOperation.Revoke))
                .ToList();

            await _grants
                .ApplyGrantsAsync(Profile, password, DatabaseName, roleName, changes, cancellationToken)
                .ConfigureAwait(true);

            _pendingByRole.Remove(roleName);

            // If this was the visible role, refresh its rows; if not, the cached entry is gone
            // so the sticky bar's pill will simply disappear.
            if (_selectedRole?.Name == roleName)
                await LoadGrantsForSelectedRoleAsync(cancellationToken).ConfigureAwait(true);

            var template = Localization["Permissions.AppliedFormat"] is { Length: > 0 } t
                ? t : "Applied {0} change(s).";
            StatusMessage = string.Format(template, changes.Count) + $" ({roleName})";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsApplying = false;
            RaisePendingAggregates();
        }
    }

    /// <summary>
    /// Apply every pending edit across every role the user has staged in this session.
    /// Each role's edits go through ApplyGrantsAsync as its own transaction — if one role's
    /// batch fails we stop early so the remaining edits stay in cache for the user to retry.
    /// </summary>
    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        // Snapshot the current row's edits under the selected role's cache slot before we start —
        // ApplyAsync iterates the cache, so the visible row needs to participate too.
        if (_selectedRole is not null)
            CapturePendingForRole(_selectedRole);

        if (_pendingByRole.Count == 0)
            return;

        IsApplying = true;
        ErrorMessage = null;
        StatusMessage = null;

        var totalChanges = 0;
        var totalRoles = 0;

        try
        {
            var password = await _profiles
                .GetPasswordAsync(Profile.Id, cancellationToken)
                .ConfigureAwait(true) ?? "";

            // Snapshot keys because we mutate _pendingByRole as we go.
            var roleNames = _pendingByRole.Keys.ToList();
            foreach (var roleName in roleNames)
            {
                if (!_pendingByRole.TryGetValue(roleName, out var diff) || diff.Count == 0)
                    continue;

                // Need the original CurrentValue to know whether each pending entry is
                // a Grant or a Revoke. The cell's PendingValue *is* the desired state, so:
                //   pendingValue=true  → user wants it granted   → emit Grant
                //   pendingValue=false → user wants it revoked  → emit Revoke
                // (We only cache dirty cells, so by definition pendingValue != currentValue.)
                var changes = diff
                    .Select(kvp => new GrantChange(
                        kvp.Key.Schema,
                        kvp.Key.Privilege,
                        kvp.Value ? GrantOperation.Grant : GrantOperation.Revoke))
                    .ToList();

                await _grants
                    .ApplyGrantsAsync(Profile, password, DatabaseName, roleName, changes, cancellationToken)
                    .ConfigureAwait(true);

                _pendingByRole.Remove(roleName);
                totalChanges += changes.Count;
                totalRoles += 1;
            }

            // Refresh the visible role so its rows reflect what's now in the database.
            await LoadGrantsForSelectedRoleAsync(cancellationToken).ConfigureAwait(true);

            var template = Localization["Permissions.AppliedFormat"] is { Length: > 0 } t
                ? t : "Applied {0} change(s).";
            StatusMessage = totalRoles > 1
                ? string.Format(template, totalChanges) + $" ({totalRoles} roles)"
                : string.Format(template, totalChanges);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            // Refresh anyway so the user sees what actually went through before the failure.
            try { await LoadGrantsForSelectedRoleAsync(cancellationToken).ConfigureAwait(true); }
            catch { /* swallow — primary error already surfaced */ }
        }
        finally
        {
            IsApplying = false;
            RaisePendingAggregates();
        }
    }

    /// <summary>
    /// Recompute every aggregate that depends on cell pending state. Called whenever a child
    /// cell flips so the sticky bar / Apply button stay in sync.
    /// </summary>
    private void RaisePendingAggregates()
    {
        RaisePropertyChanged(nameof(CurrentRowsPendingCount));
        RaisePropertyChanged(nameof(PendingCount));
        RaisePropertyChanged(nameof(HasPending));
        RaisePropertyChanged(nameof(PendingGrantCount));
        RaisePropertyChanged(nameof(PendingRevokeCount));
        RaisePropertyChanged(nameof(PendingRoleCount));
        RaisePropertyChanged(nameof(HasMultiplePendingRoles));
        RaisePropertyChanged(nameof(PendingGroups));
        RaisePropertyChanged(nameof(IsContentVisible));
        ApplyCommand.RaiseCanExecuteChanged();
        DiscardCommand.RaiseCanExecuteChanged();
    }
}
