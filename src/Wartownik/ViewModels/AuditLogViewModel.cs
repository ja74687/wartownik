using System.Collections.ObjectModel;
using System.Globalization;
using Wartownik.Audit;
using Wartownik.Localization;

namespace Wartownik.ViewModels;

/// <summary>
/// One day's worth of audit entries — used by the SQL log tab and the Overview's
/// RECENT CHANGES section to render "Today / Yesterday / 3 May 2026" headers.
/// </summary>
public sealed record AuditDayGroup(string Header, IReadOnlyList<AuditEntryViewModel> Entries);

public sealed class AuditEntryViewModel : ViewModelBase
{
    private bool _isExpanded;
    private readonly ILocalizationService? _localization;

    public AuditEntry Entry { get; }

    public AuditEntryViewModel(AuditEntry entry, ILocalizationService? localization = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Entry = entry;
        _localization = localization;
        ToggleExpandCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetField(ref _isExpanded, value))
                RaisePropertyChanged(nameof(ChevronGlyph));
        }
    }

    public RelayCommand ToggleExpandCommand { get; }

    public string ChevronGlyph => _isExpanded ? "▾" : "▸";

    public string TimeText => Entry.Timestamp.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);

    public string Headline =>
        Entry.Outcome == AuditOutcome.Success
            ? $"Applied {Entry.Statements.Count} statement(s) to {Entry.TargetRoleName}"
            : $"Failed: {Entry.Statements.Count} statement(s) for {Entry.TargetRoleName}";

    public string Subline =>
        $"by {Entry.Executor}" + (string.IsNullOrEmpty(Entry.ErrorMessage) ? "" : $" — {Entry.ErrorMessage}");

    public bool IsSuccess => Entry.Outcome == AuditOutcome.Success;
    public bool IsFailure => Entry.Outcome == AuditOutcome.Failed;

    public string OutcomeGlyph => IsSuccess ? "✓" : "✕";
    public string OutcomeColorHex => IsSuccess ? "#10B981" : "#EF4444";
}

/// <summary>
/// Lists audit entries for one (profile, optional database) scope. Loads on demand and
/// regroups by local date so the UI shows "TODAY", "YESTERDAY", or a date header per group.
/// </summary>
public sealed class AuditLogViewModel : ViewModelBase
{
    private readonly IAuditLogStore _store;
    private readonly ILocalizationService _localization;
    private readonly Guid _profileId;
    private readonly string? _databaseName;
    private readonly int _max;

    private bool _isLoading;
    private string? _errorMessage;

    public ObservableCollection<AuditDayGroup> Groups { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }

    public AuditLogViewModel(
        IAuditLogStore store,
        ILocalizationService localization,
        Guid profileId,
        string? databaseName = null,
        int max = 200)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(localization);
        _store = store;
        _localization = localization;
        _profileId = profileId;
        _databaseName = databaseName;
        _max = max;
        RefreshCommand = new AsyncRelayCommand(() => LoadAsync());
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetField(ref _isLoading, value))
            {
                RaisePropertyChanged(nameof(IsContentVisible));
                RaisePropertyChanged(nameof(IsEmpty));
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
                RaisePropertyChanged(nameof(IsEmpty));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_errorMessage);
    public bool IsContentVisible => !IsLoading && !HasError;
    public bool IsEmpty => !IsLoading && !HasError && Groups.Count == 0;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        ErrorMessage = null;
        Groups.Clear();
        RaisePropertyChanged(nameof(IsEmpty));

        try
        {
            var entries = await _store
                .ListAsync(_profileId, _databaseName, _max, cancellationToken)
                .ConfigureAwait(true);

            // Already newest-first from the store; group by local date.
            var today = DateTimeOffset.Now.Date;
            var yesterday = today.AddDays(-1);

            var grouped = entries
                .GroupBy(e => e.Timestamp.ToLocalTime().Date)
                .OrderByDescending(g => g.Key);

            foreach (var group in grouped)
            {
                var header = group.Key == today
                    ? LocalizedOr("AuditLog.Today", "TODAY")
                    : group.Key == yesterday
                        ? LocalizedOr("AuditLog.Yesterday", "YESTERDAY")
                        : group.Key.ToString("d MMMM yyyy", CultureInfo.CurrentCulture).ToUpperInvariant();

                var items = group
                    .Select(e => new AuditEntryViewModel(e, _localization))
                    .ToList();
                Groups.Add(new AuditDayGroup(header, items));
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
            RaisePropertyChanged(nameof(IsEmpty));
        }
    }

    private string LocalizedOr(string key, string fallback)
    {
        var v = _localization[key];
        return string.IsNullOrEmpty(v) || v == key ? fallback : v;
    }
}
