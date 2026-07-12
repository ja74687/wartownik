using System.Collections.ObjectModel;
using System.Globalization;
using Wartownik.Audit;
using Wartownik.Connections;
using Wartownik.Dialogs;
using Wartownik.Localization;
using Wartownik.Yaml;

namespace Wartownik.ViewModels;

/// <summary>
/// Heuristic safety score surfaced in AT A GLANCE.
/// Unknown — we couldn't compute (null roles list).
/// Ok — nothing flagged.
/// High — at least one login role on the cluster is also a SUPERUSER (it can drop the database
/// or its objects despite any GRANT/REVOKE we do later).
/// </summary>
public enum RiskLevel
{
    Unknown,
    Ok,
    High,
}

public sealed class DatabaseDetailsViewModel : ViewModelBase
{
    private readonly IConnectionProfileService _profiles;
    private readonly IPostgresMetadataService _metadata;
    private readonly IPostgresGrantService? _grants;
    private readonly IConnectionTester? _tester;
    private readonly IPreviewSqlDialog? _previewSqlDialog;
    private readonly IAuditLogStore? _auditLog;
    private readonly IYamlExporter? _yamlExporter;
    private readonly IYamlExportDialog? _yamlExportDialog;
    private readonly IConfirmationDialog? _confirmation;

    private bool _isLoading;
    private string? _errorMessage;
    private string? _testStatus;
    private bool _testInProgress;
    private DatabaseSummary _summary;
    private int _selectedTabIndex;
    private PermissionsMatrixViewModel? _permissionsMatrix;
    private AuditLogViewModel? _sqlLog;
    private AuditLogViewModel? _recentChanges;
    private int? _loginUserCount;
    private DateTimeOffset? _lastApplyAt;
    private RiskLevel _riskLevel;

    public ConnectionProfile Profile { get; }
    public string DatabaseName => _summary.Name;
    public DatabaseSummary Summary => _summary;
    public ILocalizationService Localization { get; }
    public ObservableCollection<SchemaItemViewModel> Schemas { get; } = new();

    public AsyncRelayCommand TestConnectionCommand { get; }
    public AsyncRelayCommand ExportYamlCommand { get; }

    public DatabaseDetailsViewModel(
        ConnectionProfile profile,
        DatabaseSummary summary,
        ILocalizationService localization,
        IConnectionProfileService profiles,
        IPostgresMetadataService metadata,
        IConnectionTester? tester = null,
        IPostgresGrantService? grants = null,
        IPreviewSqlDialog? previewSqlDialog = null,
        IAuditLogStore? auditLog = null,
        IYamlExporter? yamlExporter = null,
        IYamlExportDialog? yamlExportDialog = null,
        IConfirmationDialog? confirmation = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary.Name);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(metadata);

        Profile = profile;
        _summary = summary;
        Localization = localization;
        _profiles = profiles;
        _metadata = metadata;
        _tester = tester;
        _grants = grants;
        _previewSqlDialog = previewSqlDialog;
        _auditLog = auditLog;
        _yamlExporter = yamlExporter;
        _yamlExportDialog = yamlExportDialog;
        _confirmation = confirmation;

        Schemas.CollectionChanged += (_, _) =>
        {
            RaisePropertyChanged(nameof(SchemaCount));
            RaisePropertyChanged(nameof(SchemaCountText));
            RaisePropertyChanged(nameof(MetadataLine));
        };

        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync);
        ExportYamlCommand = new AsyncRelayCommand(ExportYamlAsync,
            () => _yamlExporter is not null && _yamlExportDialog is not null);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetField(ref _isLoading, value))
            {
                RaisePropertyChanged(nameof(IsContentVisible));
                RaisePropertyChanged(nameof(IsSchemasEmpty));
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value))
            {
                RaisePropertyChanged(nameof(HasError));
                RaisePropertyChanged(nameof(IsContentVisible));
                RaisePropertyChanged(nameof(IsSchemasEmpty));
            }
        }
    }

    public string? TestStatus
    {
        get => _testStatus;
        private set
        {
            if (SetField(ref _testStatus, value))
                RaisePropertyChanged(nameof(HasTestStatus));
        }
    }

    public bool HasTestStatus => !string.IsNullOrEmpty(_testStatus);

    public bool TestInProgress
    {
        get => _testInProgress;
        private set => SetField(ref _testInProgress, value);
    }

    public bool HasError => !string.IsNullOrEmpty(_errorMessage);
    public bool HasSchemas => Schemas.Count > 0;
    public bool IsSchemasEmpty => !IsLoading && !HasError && !HasSchemas;
    public bool IsContentVisible => !IsLoading && !HasError;

    public string Endpoint => $"{Profile.Host}:{Profile.Port} / {DatabaseName} / {Profile.Username}";

    /// <summary>
    /// Bound to TabControl.SelectedIndex on the database workspace. Tab order:
    /// 0 = Overview, 1 = Schemas, 2 = Permissions, 3 = SQL log.
    /// We trigger the first PermissionsMatrix load lazily when the user clicks the tab so
    /// we don't fetch every role's grants for users who never open the matrix.
    /// </summary>
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (!SetField(ref _selectedTabIndex, value))
                return;
            // Lazy-load tab content the first time the user opens it.
            switch (value)
            {
                case 2: _ = EnsurePermissionsLoadedAsync(); break;
                case 3: _ = EnsureSqlLogLoadedAsync(); break;
            }
        }
    }

    public PermissionsMatrixViewModel? PermissionsMatrix
    {
        get => _permissionsMatrix;
        private set
        {
            if (SetField(ref _permissionsMatrix, value))
                RaisePropertyChanged(nameof(HasPermissionsMatrix));
        }
    }

    public bool HasPermissionsMatrix => _permissionsMatrix is not null;

    public AuditLogViewModel? SqlLog
    {
        get => _sqlLog;
        private set
        {
            if (SetField(ref _sqlLog, value))
                RaisePropertyChanged(nameof(HasSqlLog));
        }
    }

    public bool HasSqlLog => _sqlLog is not null;

    /// <summary>
    /// Compact "last 5 changes" log shown in the Overview tab. Same backing store as the
    /// full SQL log but capped so the Overview stays glanceable.
    /// </summary>
    public AuditLogViewModel? RecentChanges
    {
        get => _recentChanges;
        private set
        {
            if (SetField(ref _recentChanges, value))
                RaisePropertyChanged(nameof(HasRecentChanges));
        }
    }

    public bool HasRecentChanges => _recentChanges is not null;

    private Task EnsureSqlLogLoadedAsync()
    {
        if (_auditLog is null)
            return Task.CompletedTask;
        if (_sqlLog is null)
        {
            var vm = new AuditLogViewModel(_auditLog, Localization, Profile.Id, DatabaseName);
            SqlLog = vm;
            return vm.LoadAsync();
        }
        return _sqlLog.LoadAsync(); // refresh on re-entry
    }

    private Task EnsurePermissionsLoadedAsync()
    {
        if (_grants is null)
            return Task.CompletedTask;

        // First time the user opens the tab — build the VM and kick off its load.
        // Subsequent visits are no-ops; the user can still hit the matrix's own refresh path.
        if (_permissionsMatrix is null)
        {
            var matrix = new PermissionsMatrixViewModel(
                Profile, DatabaseName, Localization, _profiles, _metadata, _grants,
                _previewSqlDialog, _auditLog, _confirmation);

            // Forward matrix's pending counters to AT A GLANCE so the Overview reflects
            // whatever the user has staged in the Permissions tab — without us having to
            // duplicate the counters across both VMs.
            // We also refresh the recent-changes log when a matrix apply settles back to 0,
            // so the Overview's audit list shows the just-recorded entry without manual reload.
            matrix.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PermissionsMatrixViewModel.PendingCount))
                {
                    RaisePropertyChanged(nameof(PendingCount));
                    RaisePropertyChanged(nameof(PendingCountText));
                    RaisePropertyChanged(nameof(HasPendingChanges));
                }
                else if (e.PropertyName == nameof(PermissionsMatrixViewModel.IsApplying)
                         && !matrix.IsApplying)
                {
                    _ = RefreshRecentChangesAsync();
                    _ = RefreshLastApplyAsync();
                }
            };

            PermissionsMatrix = matrix;
            return matrix.LoadAsync();
        }
        return Task.CompletedTask;
    }

    // -- Header / At-a-glance computed --

    public string UserInitial => string.IsNullOrEmpty(Profile.Username) ? "?" : Profile.Username[..1].ToUpperInvariant();

    public string? Owner => _summary.Owner;

    /// <summary>
    /// True when the connecting user is also the database owner — useful to surface in the UI
    /// because owner connections imply a much wider blast radius for any changes.
    /// </summary>
    public bool IsConnectingAsOwner =>
        !string.IsNullOrEmpty(Owner) &&
        string.Equals(Owner, Profile.Username, StringComparison.Ordinal);

    /// <summary>
    /// Caption shown on the connection card. Empty when the connecting user is the owner
    /// (already surfaced by the OWNER pill) so we don't repeat the same info twice.
    /// </summary>
    public string OwnerLine =>
        string.IsNullOrEmpty(Owner) || IsConnectingAsOwner
            ? ""
            : string.Format(
                CultureInfo.CurrentCulture,
                LocalizedOr("Overview.OwnedBy", "Database owner: {0}"),
                Owner);
    public string? ServerVersion => _summary.ServerVersion;
    public string SizeText => FormatSize(_summary.SizeBytes);

    public int SchemaCount => Schemas.Count;
    // Iter 6 will fill GrantCount with a real cluster-wide aggregation; for now stays at zero.
    public int GrantCount => 0;
    /// <summary>
    /// Total staged-but-not-applied edits across the matrix's tracked roles. Sourced from
    /// the lazy PermissionsMatrix VM; until the user opens the Permissions tab there's no
    /// matrix to ask, so we report 0.
    /// </summary>
    public int PendingCount => _permissionsMatrix?.PendingCount ?? 0;
    public bool HasPendingChanges => PendingCount > 0;

    // Display strings for AT A GLANCE. Real metrics show the number;
    // placeholders show an em-dash so we don't lie about "0 grants" before later iters fill them in.
    public string SchemaCountText => SchemaCount.ToString(CultureInfo.CurrentCulture);
    public string GrantCountText => "—";       // Iter 6 — needs cluster-wide grant aggregation
    /// <summary>
    /// Em-dash before the user has opened Permissions (PendingCount has nothing to report);
    /// once the matrix is alive we surface the live number, including 0.
    /// </summary>
    public string PendingCountText => _permissionsMatrix is null
        ? "—"
        : PendingCount.ToString(CultureInfo.CurrentCulture);
    /// <summary>
    /// Number of login roles on the cluster. Em-dash before the background fetch finishes
    /// or if the fetch failed — we don't surface zero on a failure since "0 users" reads as
    /// a definitive answer.
    /// </summary>
    public string LoginUserCountText => _loginUserCount.HasValue
        ? _loginUserCount.Value.ToString(CultureInfo.CurrentCulture)
        : "—";

    public DateTimeOffset? LastApplyAt
    {
        get => _lastApplyAt;
        private set
        {
            if (SetField(ref _lastApplyAt, value))
                RaisePropertyChanged(nameof(LastApplyText));
        }
    }

    /// <summary>
    /// Relative time since Wartownik last applied here. "never" when no audit entries match.
    /// </summary>
    public string LastApplyText
    {
        get
        {
            if (!_lastApplyAt.HasValue)
                return LocalizedOr("Overview.NeverApplied", "never");
            var delta = DateTimeOffset.UtcNow - _lastApplyAt.Value.ToUniversalTime();
            if (delta.TotalSeconds < 60) return "just now";
            if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes} min ago";
            if (delta.TotalHours < 24) return $"{(int)delta.TotalHours} h ago";
            if (delta.TotalDays < 30) return $"{(int)delta.TotalDays} d ago";
            return _lastApplyAt.Value.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.CurrentCulture);
        }
    }

    public RiskLevel RiskLevel
    {
        get => _riskLevel;
        private set
        {
            if (SetField(ref _riskLevel, value))
            {
                RaisePropertyChanged(nameof(RiskText));
                RaisePropertyChanged(nameof(IsRiskOk));
                RaisePropertyChanged(nameof(IsRiskHigh));
                RaisePropertyChanged(nameof(IsRiskUnknown));
            }
        }
    }

    public string RiskText => _riskLevel switch
    {
        RiskLevel.Ok      => LocalizedOr("Overview.RiskOk", "OK"),
        RiskLevel.High    => LocalizedOr("Overview.RiskHigh", "HIGH"),
        _                  => "—",
    };

    public bool IsRiskOk      => _riskLevel == RiskLevel.Ok;
    public bool IsRiskHigh    => _riskLevel == RiskLevel.High;
    public bool IsRiskUnknown => _riskLevel == RiskLevel.Unknown;

    /// <summary>
    /// Subtitle line under the database name. Same dot-separated format as the database card,
    /// extended with live schema count once schemas are loaded.
    /// </summary>
    public string MetadataLine
    {
        get
        {
            var parts = new List<string>(5);
            if (!string.IsNullOrEmpty(Owner))
                parts.Add($"{LocalizedOr("Databases.OwnerPrefix", "Owner")}: {Owner}");
            if (SchemaCount > 0)
                parts.Add(FormatCount(SchemaCount, "Databases.SchemasOne", "Databases.SchemasMany", "schema", "schemas"));
            if (GrantCount > 0)
                parts.Add(FormatCount(GrantCount, "Databases.GrantsOne", "Databases.GrantsMany", "grant", "grants"));
            if (_summary.SizeBytes.HasValue)
                parts.Add($"{LocalizedOr("Databases.SizePrefix", "Size")} {SizeText}");
            return string.Join(" · ", parts);
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        ErrorMessage = null;
        Schemas.Clear();
        RaisePropertyChanged(nameof(HasSchemas));
        RaisePropertyChanged(nameof(IsSchemasEmpty));

        try
        {
            var password = await _profiles
                .GetPasswordAsync(Profile.Id, cancellationToken)
                .ConfigureAwait(true) ?? "";

            var loaded = await _metadata
                .ListSchemasAsync(Profile, password, DatabaseName, cancellationToken)
                .ConfigureAwait(true);

            foreach (var summary in loaded)
                Schemas.Add(new SchemaItemViewModel(summary));

            // Boot the Overview's recent-changes log eagerly — it's cheap (reads a small JSONL
            // file) and it's the first thing the user sees when they open the database.
            if (_auditLog is not null)
            {
                if (_recentChanges is null)
                    RecentChanges = new AuditLogViewModel(_auditLog, Localization, Profile.Id, DatabaseName, max: 5);
                await _recentChanges!.LoadAsync(cancellationToken).ConfigureAwait(true);
            }

            // Fire-and-forget the AT A GLANCE secondary stats — they're not critical and we
            // don't want to block the Overview render on them.
            _ = RefreshAtAGlanceStatsAsync(password, cancellationToken);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
            RaisePropertyChanged(nameof(HasSchemas));
            RaisePropertyChanged(nameof(IsSchemasEmpty));
        }
    }

    /// <summary>
    /// Refresh the Overview's recent-changes log — called after a successful Apply so the
    /// just-recorded entry shows up immediately.
    /// </summary>
    private async Task RefreshRecentChangesAsync()
    {
        if (_recentChanges is null)
            return;
        try { await _recentChanges.LoadAsync().ConfigureAwait(true); }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// Re-pull the latest audit timestamp so AT A GLANCE's "last apply" shows "just now"
    /// the moment a matrix Apply finishes, without waiting for a full re-load of the page.
    /// </summary>
    private async Task RefreshLastApplyAsync()
    {
        if (_auditLog is null)
            return;
        try
        {
            var lastEntries = await _auditLog
                .ListAsync(Profile.Id, DatabaseName, max: 1)
                .ConfigureAwait(true);
            LastApplyAt = lastEntries.Count > 0 ? lastEntries[0].Timestamp : null;
        }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// Background-fetch the secondary AT A GLANCE stats. Each leg is best-effort —
    /// failures degrade to em-dash rather than blowing up the whole Overview.
    /// </summary>
    private async Task RefreshAtAGlanceStatsAsync(string password, CancellationToken cancellationToken)
    {
        // Login users + risk derive from cluster role list.
        try
        {
            var roles = await _metadata
                .ListRolesAsync(Profile, password, cancellationToken)
                .ConfigureAwait(true);

            var loginRoles = roles.Where(r => r.CanLogin).ToList();
            _loginUserCount = loginRoles.Count;
            RaisePropertyChanged(nameof(LoginUserCountText));

            // Heuristic: any login role that's also a SUPERUSER is a privilege-bypass risk —
            // they sidestep every GRANT/REVOKE we issue. More heuristics can pile in later.
            RiskLevel = loginRoles.Any(r => r.IsSuperuser) ? RiskLevel.High : RiskLevel.Ok;
        }
        catch
        {
            _loginUserCount = null;
            RaisePropertyChanged(nameof(LoginUserCountText));
            RiskLevel = RiskLevel.Unknown;
        }

        // Last apply timestamp from the audit log (filtered to this database).
        if (_auditLog is null)
            return;
        try
        {
            var lastEntries = await _auditLog
                .ListAsync(Profile.Id, DatabaseName, max: 1, cancellationToken)
                .ConfigureAwait(true);
            LastApplyAt = lastEntries.Count > 0 ? lastEntries[0].Timestamp : null;
        }
        catch
        {
            // leave LastApplyAt null → "never"
        }
    }

    /// <summary>
    /// Snapshot the current state of the database's privileges and hand the YAML to the
    /// preview dialog. The dialog handles save-to-file / copy-to-clipboard from there.
    /// </summary>
    private async Task ExportYamlAsync()
    {
        if (_yamlExporter is null || _yamlExportDialog is null)
            return;
        try
        {
            var password = await _profiles
                .GetPasswordAsync(Profile.Id)
                .ConfigureAwait(true) ?? "";

            var yaml = await _yamlExporter
                .ExportAsync(Profile, password, DatabaseName)
                .ConfigureAwait(true);

            // Filename pattern: <profile>-<db>-YYYYMMDD-HHmm.yaml — readable + sortable.
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmm");
            var slug = $"{Sanitize(Profile.DisplayName)}-{Sanitize(DatabaseName)}-{stamp}.yaml";

            await _yamlExportDialog
                .ShowAsync(new YamlExportRequest(slug, yaml))
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private static string Sanitize(string input) =>
        string.Concat(input.Select(c => char.IsLetterOrDigit(c) ? c : '_'));

    private async Task TestConnectionAsync()
    {
        if (_tester is null)
            return;

        TestInProgress = true;
        TestStatus = Localization["Test.InProgress"];

        try
        {
            var password = await _profiles
                .GetPasswordAsync(Profile.Id)
                .ConfigureAwait(true) ?? "";
            var profileForDb = Profile with { Database = DatabaseName };
            var result = await _tester
                .TestAsync(profileForDb, password)
                .ConfigureAwait(true);

            TestStatus = result.Success
                ? Localization["Test.Success"]
                : $"{Localization["Test.Failed"]} {result.ErrorMessage}";
        }
        catch (Exception ex)
        {
            TestStatus = $"{Localization["Test.Failed"]} {ex.Message}";
        }
        finally
        {
            TestInProgress = false;
        }
    }

    private string LocalizedOr(string key, string fallback)
    {
        var value = Localization[key];
        return string.IsNullOrEmpty(value) || value == key ? fallback : value;
    }

    private string FormatCount(int value, string oneKey, string manyKey, string oneFallback, string manyFallback)
    {
        var template = value == 1
            ? LocalizedOr(oneKey, "{0} " + oneFallback)
            : LocalizedOr(manyKey, "{0} " + manyFallback);
        return string.Format(CultureInfo.CurrentCulture, template, value);
    }

    private static string FormatSize(long? bytes)
    {
        if (!bytes.HasValue)
            return "";
        var b = bytes.Value;
        if (b < 1024) return $"{b} B";
        double kb = b / 1024.0;
        if (kb < 1024) return $"{kb:0.#} KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:0.#} MB";
        double gb = mb / 1024.0;
        if (gb < 1024) return $"{gb:0.##} GB";
        double tb = gb / 1024.0;
        return $"{tb:0.##} TB";
    }
}
