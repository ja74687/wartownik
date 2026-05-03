using System.Collections.ObjectModel;
using System.Globalization;
using Wartownik.Connections;
using Wartownik.Localization;

namespace Wartownik.ViewModels;

public sealed class DatabaseDetailsViewModel : ViewModelBase
{
    private readonly IConnectionProfileService _profiles;
    private readonly IPostgresMetadataService _metadata;
    private readonly IPostgresGrantService? _grants;
    private readonly IConnectionTester? _tester;

    private bool _isLoading;
    private string? _errorMessage;
    private string? _testStatus;
    private bool _testInProgress;
    private DatabaseSummary _summary;
    private int _selectedTabIndex;
    private PermissionsMatrixViewModel? _permissionsMatrix;

    public ConnectionProfile Profile { get; }
    public string DatabaseName => _summary.Name;
    public DatabaseSummary Summary => _summary;
    public ILocalizationService Localization { get; }
    public ObservableCollection<SchemaItemViewModel> Schemas { get; } = new();

    public AsyncRelayCommand TestConnectionCommand { get; }

    public DatabaseDetailsViewModel(
        ConnectionProfile profile,
        DatabaseSummary summary,
        ILocalizationService localization,
        IConnectionProfileService profiles,
        IPostgresMetadataService metadata,
        IConnectionTester? tester = null,
        IPostgresGrantService? grants = null)
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

        Schemas.CollectionChanged += (_, _) =>
        {
            RaisePropertyChanged(nameof(SchemaCount));
            RaisePropertyChanged(nameof(SchemaCountText));
            RaisePropertyChanged(nameof(MetadataLine));
        };

        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync);
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
            if (SetField(ref _selectedTabIndex, value) && value == 2)
                _ = EnsurePermissionsLoadedAsync();
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

    private Task EnsurePermissionsLoadedAsync()
    {
        if (_grants is null)
            return Task.CompletedTask;

        // First time the user opens the tab — build the VM and kick off its load.
        // Subsequent visits are no-ops; the user can still hit the matrix's own refresh path.
        if (_permissionsMatrix is null)
        {
            var matrix = new PermissionsMatrixViewModel(
                Profile, DatabaseName, Localization, _profiles, _metadata, _grants);
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
    // Iter 5 will populate these — for now scaffold with zero so AT A GLANCE renders correctly.
    public int GrantCount => 0;
    public int PendingCount => 0;
    public bool HasPendingChanges => PendingCount > 0;

    // Display strings for AT A GLANCE. Real metrics show the number;
    // placeholders show an em-dash so we don't lie about "0 grants" before later iters fill them in.
    public string SchemaCountText => SchemaCount.ToString(CultureInfo.CurrentCulture);
    public string GrantCountText => "—";       // Iter 5
    public string PendingCountText => "—";     // Iter 5
    public string LoginUserCountText => "—";   // Iter 6 — distinct login roles with privileges here
    public string LastApplyText => LocalizedOr("Overview.NeverApplied", "never"); // Iter 6 — last Wartownik apply
    public string RiskText => "—";             // Iter 6 — heuristic: SUPERUSER login w/ access, etc.

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
