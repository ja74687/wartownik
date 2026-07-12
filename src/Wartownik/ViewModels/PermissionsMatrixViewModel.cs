using System.Collections.ObjectModel;
using Wartownik.Audit;
using Wartownik.Connections;
using Wartownik.Dialogs;
using Wartownik.Localization;

namespace Wartownik.ViewModels;

/// <summary>
/// One role's block in the sticky bar's pending list: the role name plus its individual
/// staged changes, each independently selectable for "Apply selected".
/// </summary>
public sealed record PendingGroup(string RoleName, IReadOnlyList<PendingChangeViewModel> Changes)
{
    public int TotalCount => Changes.Count;
    public int SelectedCount => Changes.Count(c => c.IsSelected);
    public int GrantCount => Changes.Count(c => c.IsGrant);
    public int RevokeCount => Changes.Count(c => c.IsRevoke);
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
    private readonly IPreviewSqlDialog? _previewSqlDialog;
    private readonly IAuditLogStore? _auditLog;
    private readonly IConfirmationDialog? _confirmation;

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

    // Guards for the fire-and-forget grants load. _loadToken makes a superseded load abandon
    // instead of clobbering fresher Rows; _rowsRole records which role the live Rows actually
    // represent, so we never capture one role's dirty cells under another's name mid-load.
    private int _loadToken;
    private string? _rowsRole;

    /// <summary>
    /// Changes the user has un-checked in the sticky bar. Everything is selected by default,
    /// so we only track the opt-outs. Pruned to currently-pending keys on every aggregate refresh.
    /// </summary>
    private readonly HashSet<(string Role, string Schema, GrantPrivilege Privilege)> _deselected = new();

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
    public AsyncRelayCommand PreviewSqlCommand { get; }
    public AsyncRelayCommand ApplySelectedCommand { get; }

    public PermissionsMatrixViewModel(
        ConnectionProfile profile,
        string databaseName,
        ILocalizationService localization,
        IConnectionProfileService profiles,
        IPostgresMetadataService metadata,
        IPostgresGrantService grants,
        IPreviewSqlDialog? previewSqlDialog = null,
        IAuditLogStore? auditLog = null,
        IConfirmationDialog? confirmation = null)
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
        _previewSqlDialog = previewSqlDialog;
        _auditLog = auditLog;
        _confirmation = confirmation;

        LoadCommand = new AsyncRelayCommand(() => LoadAsync());
        ApplyCommand = new AsyncRelayCommand(() => ApplyAsync(), () => HasPending && !_isApplying);
        DiscardCommand = new RelayCommand(Discard, () => HasPending && !_isApplying);
        ApplyRoleCommand = new AsyncRelayCommand(p => ApplyRoleAsync(p as string));
        DiscardRoleCommand = new RelayCommand(p => DiscardRole(p as string));
        PreviewSqlCommand = new AsyncRelayCommand(() => PreviewSqlAsync(), () => HasSelectedChanges && _previewSqlDialog is not null);
        ApplySelectedCommand = new AsyncRelayCommand(() => ApplySelectedAsync(), () => HasSelectedChanges && !_isApplying);
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
            RaisePropertyChanged(nameof(IsSelectedRoleSuperuser));

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
        // Only capture when the live Rows actually represent this role. During an in-flight load
        // the Rows may still show the previous role; capturing then would stash the wrong role's
        // dirty cells under this name (a phantom edit the user never made for it).
        if (_rowsRole != role.Name)
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
    private void RestorePendingForRole(RoleSummary role)
    {
        if (!_pendingByRole.TryGetValue(role.Name, out var cached))
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

        // The role's pending now lives in the live Rows, so drop its cache entry —
        // otherwise PendingCount (= live rows + cache) would count this role twice.
        _pendingByRole.Remove(role.Name);
    }

    public bool HasSelectedRole => _selectedRole is not null;

    /// <summary>
    /// True when the picked target role is a superuser. Superusers bypass all permission checks
    /// in Postgres, so the matrix is effectively decorative for them — GRANT/REVOKE statements
    /// would succeed but change nothing in practice. UI surfaces a warning banner.
    /// </summary>
    public bool IsSelectedRoleSuperuser => _selectedRole?.IsSuperuser == true;

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
    /// Every individual pending change across all roles — the cached (non-selected) roles plus
    /// the live edits of the currently-selected role's rows. Read-only; does not mutate state.
    /// The RestorePendingForRole invariant guarantees the selected role is never also in
    /// the cache, so nothing is double-yielded.
    /// </summary>
    private IEnumerable<(string Role, string Schema, GrantPrivilege Privilege, bool IsGrant)> EnumerateAllPending()
    {
        foreach (var (roleName, diff) in _pendingByRole)
            foreach (var kvp in diff)
                yield return (roleName, kvp.Key.Schema, kvp.Key.Privilege, kvp.Value);

        if (_selectedRole is not null)
        {
            foreach (var row in Rows)
                foreach (var cell in row.Cells)
                    if (cell.IsDirty)
                        yield return (_selectedRole.Name, row.SchemaName, cell.Privilege, cell.PendingValue);
        }
    }

    private bool IsChangeSelected(string role, string schema, GrantPrivilege priv) =>
        !_deselected.Contains((role, schema, priv));

    /// <summary>
    /// Pending edits grouped by target role, each carrying its individual selectable changes.
    /// Rebuilt whenever the pending set changes; individual selection survives because it lives
    /// in <see cref="_deselected"/> rather than on the rebuilt change VMs.
    /// </summary>
    public IReadOnlyList<PendingGroup> PendingGroups =>
        EnumerateAllPending()
            .GroupBy(c => c.Role, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new PendingGroup(
                g.Key,
                g.OrderBy(c => c.Schema, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(c => c.Privilege)
                    .Select(c => new PendingChangeViewModel(
                        c.Role, c.Schema, c.Privilege, c.IsGrant,
                        isSelected: IsChangeSelected(c.Role, c.Schema, c.Privilege),
                        onSelectionChanged: OnChangeSelectionToggled))
                    .ToList()))
            .ToList();

    /// <summary>Pending changes the user has left ticked — what "Apply selected" will run.</summary>
    public int SelectedChangeCount =>
        EnumerateAllPending().Count(c => IsChangeSelected(c.Role, c.Schema, c.Privilege));

    public bool HasSelectedChanges => SelectedChangeCount > 0;

    public string SelectionSummary =>
        string.Format(
            LocalizedOr("Permissions.SelectionSummary", "{0} selected of {1} pending · {2} role(s)"),
            SelectedChangeCount, PendingCount, PendingRoleCount);

    private void OnChangeSelectionToggled(PendingChangeViewModel change)
    {
        var key = (change.RoleName, change.SchemaName, change.Privilege);
        if (change.IsSelected)
            _deselected.Remove(key);
        else
            _deselected.Add(key);

        // Refresh selection-dependent state only — NOT PendingGroups, which would rebuild the
        // checkbox VMs out from under the click that just happened.
        RaiseSelectionAggregates();
    }

    private void RaiseSelectionAggregates()
    {
        RaisePropertyChanged(nameof(SelectedChangeCount));
        RaisePropertyChanged(nameof(HasSelectedChanges));
        RaisePropertyChanged(nameof(SelectionSummary));
        ApplySelectedCommand.RaiseCanExecuteChanged();
        PreviewSqlCommand.RaiseCanExecuteChanged();
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
        // Mark this as the newest load and snapshot the role we're loading. A switch that happens
        // while we're awaiting bumps _loadToken and repoints _selectedRole; we must not let this
        // stale load then clobber the fresher Rows or restore against the wrong role.
        var token = ++_loadToken;
        var role = _selectedRole;
        _rowsRole = null; // the live Rows no longer faithfully represent any committed role

        if (role is null)
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
                .ListSchemaGrantsAsync(Profile, password, DatabaseName, role.Name, cancellationToken)
                .ConfigureAwait(true);

            // A newer switch superseded us while awaiting — abandon without touching Rows so the
            // newer load owns them.
            if (token != _loadToken)
                return;

            Rows.Clear();
            foreach (var summary in summaries)
                Rows.Add(new SchemaPermissionRowViewModel(summary, RaisePendingAggregates));

            // Reapply any edits the user had staged for this role earlier in the session.
            RestorePendingForRole(role);
            _rowsRole = role.Name; // Rows now faithfully represent `role`
            RaisePendingAggregates();
        }
        catch (Exception ex)
        {
            if (token == _loadToken)
                ErrorMessage = ex.Message;
        }
        finally
        {
            if (token == _loadToken)
                IsLoading = false;
        }
    }

    /// <summary>
    /// Build the same statement batch that Apply would run, group it by role, and hand it
    /// to the preview dialog. Read-only — closing the dialog does not commit anything.
    /// </summary>
    public async Task PreviewSqlAsync(CancellationToken cancellationToken = default)
    {
        if (_previewSqlDialog is null)
            return;

        // Build the ticked subset per role from the merged pending view — read-only, no capture
        // and no reload, so previewing never disturbs the matrix.
        var byRole = new Dictionary<string, List<GrantChange>>(StringComparer.Ordinal);
        foreach (var c in EnumerateAllPending())
        {
            if (_deselected.Contains((c.Role, c.Schema, c.Privilege)))
                continue;
            if (!byRole.TryGetValue(c.Role, out var list))
                byRole[c.Role] = list = new List<GrantChange>();
            list.Add(new GrantChange(c.Schema, c.Privilege, c.IsGrant ? GrantOperation.Grant : GrantOperation.Revoke));
        }

        if (byRole.Count == 0)
            return;

        var groups = new List<PreviewSqlGroup>();
        var previewedChanges = 0;
        foreach (var roleName in byRole.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            var changes = byRole[roleName];
            previewedChanges += changes.Count;
            groups.Add(new PreviewSqlGroup(roleName, PostgresGrantService.BuildStatements(roleName, changes)));
        }

        var title = string.Format(
            Localization["Permissions.PreviewTitleFormat"] is { Length: > 0 } template
                ? template : "{0} pending changes across {1} role(s)",
            previewedChanges,
            groups.Count);

        var request = new PreviewSqlRequest(groups, title);
        await _previewSqlDialog.ShowAsync(request, cancellationToken).ConfigureAwait(true);
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

        // Same revoke guard as the whole-batch path, scoped to this single role.
        var roleRevokes = diff.Count(kv => !kv.Value);
        if (!await ConfirmDestructiveAsync(roleRevokes, diff.Count, 1).ConfigureAwait(true))
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

            var statements = PostgresGrantService.BuildStatements(roleName, changes);

            try
            {
                await _grants
                    .ApplyGrantsAsync(Profile, password, DatabaseName, roleName, changes, cancellationToken)
                    .ConfigureAwait(true);
                await WriteAuditAsync(roleName, statements, AuditOutcome.Success, null).ConfigureAwait(true);
            }
            catch (Exception roleEx)
            {
                await WriteAuditAsync(roleName, statements, AuditOutcome.Failed, roleEx.Message).ConfigureAwait(true);
                throw;
            }

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

        // Guard: revoking is the only thing here that can lock people out, so if the whole-batch
        // apply contains any revoke, ask for confirmation before touching the database.
        var batchRevokes = _pendingByRole.Values.Sum(d => d.Count(kv => !kv.Value));
        if (!await ConfirmDestructiveAsync(
                batchRevokes,
                _pendingByRole.Values.Sum(d => d.Count),
                _pendingByRole.Count).ConfigureAwait(true))
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

                var statements = PostgresGrantService.BuildStatements(roleName, changes);

                try
                {
                    await _grants
                        .ApplyGrantsAsync(Profile, password, DatabaseName, roleName, changes, cancellationToken)
                        .ConfigureAwait(true);
                    await WriteAuditAsync(roleName, statements, AuditOutcome.Success, null).ConfigureAwait(true);
                }
                catch (Exception roleEx)
                {
                    // Per-role failure is logged and re-thrown so the outer catch surfaces the
                    // error and the remaining roles stay in cache for retry.
                    await WriteAuditAsync(roleName, statements, AuditOutcome.Failed, roleEx.Message).ConfigureAwait(true);
                    throw;
                }

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
    /// Apply only the changes the user has left ticked in the sticky bar. Each role's selected
    /// subset runs as its own transaction; un-ticked changes stay pending so the user can apply
    /// them later. This is the primary Apply path from the UI.
    /// </summary>
    public async Task ApplySelectedAsync(CancellationToken cancellationToken = default)
    {
        // Normalize: move the visible role's live edits into the cache so every role is handled
        // uniformly. The invariant fix in RestorePendingForRole keeps this from double-counting.
        if (_selectedRole is not null)
            CapturePendingForRole(_selectedRole);

        // Build the ticked subset per role.
        var selectedByRole = new Dictionary<string, List<GrantChange>>(StringComparer.Ordinal);
        var selectedRevokes = 0;
        foreach (var (roleName, diff) in _pendingByRole)
        {
            foreach (var kvp in diff)
            {
                if (_deselected.Contains((roleName, kvp.Key.Schema, kvp.Key.Privilege)))
                    continue;
                var isGrant = kvp.Value;
                if (!isGrant)
                    selectedRevokes++;
                if (!selectedByRole.TryGetValue(roleName, out var list))
                    selectedByRole[roleName] = list = new List<GrantChange>();
                list.Add(new GrantChange(
                    kvp.Key.Schema,
                    kvp.Key.Privilege,
                    isGrant ? GrantOperation.Grant : GrantOperation.Revoke));
            }
        }

        var totalSelected = selectedByRole.Values.Sum(l => l.Count);
        if (totalSelected == 0)
        {
            // Nothing ticked — pull the just-captured live role back onto its rows and bail.
            await LoadGrantsForSelectedRoleAsync(cancellationToken).ConfigureAwait(true);
            return;
        }

        if (!await ConfirmDestructiveAsync(selectedRevokes, totalSelected, selectedByRole.Count).ConfigureAwait(true))
        {
            await LoadGrantsForSelectedRoleAsync(cancellationToken).ConfigureAwait(true);
            return;
        }

        IsApplying = true;
        ErrorMessage = null;
        StatusMessage = null;
        var appliedChanges = 0;
        var appliedRoles = 0;

        try
        {
            var password = await _profiles
                .GetPasswordAsync(Profile.Id, cancellationToken)
                .ConfigureAwait(true) ?? "";

            foreach (var (roleName, changes) in selectedByRole)
            {
                var statements = PostgresGrantService.BuildStatements(roleName, changes);
                try
                {
                    await _grants
                        .ApplyGrantsAsync(Profile, password, DatabaseName, roleName, changes, cancellationToken)
                        .ConfigureAwait(true);
                    await WriteAuditAsync(roleName, statements, AuditOutcome.Success, null).ConfigureAwait(true);
                }
                catch (Exception roleEx)
                {
                    await WriteAuditAsync(roleName, statements, AuditOutcome.Failed, roleEx.Message).ConfigureAwait(true);
                    throw;
                }

                // Drop just the applied changes from this role's cache; keep the un-ticked ones.
                if (_pendingByRole.TryGetValue(roleName, out var diff))
                {
                    foreach (var ch in changes)
                        diff.Remove((ch.SchemaName, ch.Privilege));
                    if (diff.Count == 0)
                        _pendingByRole.Remove(roleName);
                }
                appliedChanges += changes.Count;
                appliedRoles += 1;
            }

            // Everything the user asked for went through — clear the opt-outs so whatever is
            // still pending defaults back to selected (ready to apply next time).
            _deselected.Clear();

            // Reload the visible role; RestorePendingForRole re-applies its remaining
            // (un-ticked) pending onto the freshly-loaded rows.
            await LoadGrantsForSelectedRoleAsync(cancellationToken).ConfigureAwait(true);

            var template = Localization["Permissions.AppliedFormat"] is { Length: > 0 } t
                ? t : "Applied {0} change(s).";
            StatusMessage = appliedRoles > 1
                ? string.Format(template, appliedChanges) + $" ({appliedRoles} roles)"
                : string.Format(template, appliedChanges);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
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
    /// When an apply batch contains any REVOKE, double-check with the user before running it —
    /// revoking is the one operation in this tool that can lock people out of schemas or tables.
    /// Grant-only batches, or setups where no confirmation dialog is wired (e.g. unit tests),
    /// proceed without a prompt. Returns true to proceed, false to abort.
    /// </summary>
    private async Task<bool> ConfirmDestructiveAsync(int revokeCount, int totalChanges, int roleCount)
    {
        if (_confirmation is null || revokeCount <= 0)
            return true;

        var messageTemplate = LocalizedOr(
            "Permissions.RevokeConfirmMessage",
            "This batch revokes {0} privilege(s) across {1} role(s) — {2} change(s) in total. " +
            "Revoking can lock users out of schemas or tables. Apply anyway?");

        var request = new ConfirmationRequest(
            Title: LocalizedOr("Permissions.RevokeConfirmTitle", "Revoke access?"),
            Message: string.Format(messageTemplate, revokeCount, roleCount, totalChanges),
            ConfirmLabel: LocalizedOr("Permissions.RevokeConfirmApply", "Revoke and apply"),
            CancelLabel: LocalizedOr("Common.Cancel", "Cancel"),
            IsDestructive: true);

        return await _confirmation.ConfirmAsync(request).ConfigureAwait(true);
    }

    private string LocalizedOr(string key, string fallback)
    {
        var value = Localization[key];
        return string.IsNullOrEmpty(value) || value == key ? fallback : value;
    }

    /// <summary>
    /// Append one audit entry per role applied. Best-effort: if the audit store throws we
    /// don't surface it to the user — the Apply itself already succeeded against the database
    /// and that's the source of truth.
    /// </summary>
    private async Task WriteAuditAsync(
        string roleName,
        IReadOnlyList<string> statements,
        AuditOutcome outcome,
        string? errorMessage)
    {
        if (_auditLog is null)
            return;
        try
        {
            var entry = new AuditEntry(
                Id: Guid.NewGuid(),
                Timestamp: DateTimeOffset.UtcNow,
                ProfileId: Profile.Id,
                ProfileName: Profile.DisplayName,
                DatabaseName: DatabaseName,
                TargetRoleName: roleName,
                Statements: statements,
                Outcome: outcome,
                ErrorMessage: errorMessage,
                Executor: $"{Environment.UserName}@{Environment.MachineName}");
            await _auditLog.AppendAsync(entry).ConfigureAwait(true);
        }
        catch
        {
            // Don't kill the apply path on audit failure — it's a side-channel.
        }
    }

    /// <summary>
    /// Recompute every aggregate that depends on cell pending state. Called whenever a child
    /// cell flips so the sticky bar / Apply button stay in sync.
    /// </summary>
    private void RaisePendingAggregates()
    {
        // Drop any de-selection whose change is no longer pending (cell toggled back, applied,
        // or discarded) so selection counts never drift from what's actually staged.
        if (_deselected.Count > 0)
        {
            var pendingKeys = new HashSet<(string, string, GrantPrivilege)>(
                EnumerateAllPending().Select(c => (c.Role, c.Schema, c.Privilege)));
            _deselected.RemoveWhere(k => !pendingKeys.Contains(k));
        }

        RaisePropertyChanged(nameof(CurrentRowsPendingCount));
        RaisePropertyChanged(nameof(PendingCount));
        RaisePropertyChanged(nameof(HasPending));
        RaisePropertyChanged(nameof(PendingGrantCount));
        RaisePropertyChanged(nameof(PendingRevokeCount));
        RaisePropertyChanged(nameof(PendingRoleCount));
        RaisePropertyChanged(nameof(HasMultiplePendingRoles));
        RaisePropertyChanged(nameof(PendingGroups));
        RaisePropertyChanged(nameof(SelectedChangeCount));
        RaisePropertyChanged(nameof(HasSelectedChanges));
        RaisePropertyChanged(nameof(SelectionSummary));
        RaisePropertyChanged(nameof(IsContentVisible));
        ApplyCommand.RaiseCanExecuteChanged();
        DiscardCommand.RaiseCanExecuteChanged();
        PreviewSqlCommand.RaiseCanExecuteChanged();
        ApplySelectedCommand.RaiseCanExecuteChanged();
    }
}
