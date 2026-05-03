using Wartownik.Connections;

namespace Wartownik.ViewModels;

/// <summary>
/// One row of the permissions matrix — the schema name plus six privilege checkboxes.
/// Children cells call up to <paramref name="onChanged"/> whenever they flip; the parent
/// matrix VM uses that to recompute the global pending list.
/// </summary>
public sealed class SchemaPermissionRowViewModel : ViewModelBase
{
    private readonly Action _onChanged;

    public SchemaPermissionRowViewModel(SchemaGrantSummary summary, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(onChanged);
        SchemaName = summary.SchemaName;
        _onChanged = onChanged;

        Usage  = new PrivilegeCellViewModel(GrantPrivilege.Usage,  summary.Usage,  RaiseRowAggregates);
        Create = new PrivilegeCellViewModel(GrantPrivilege.Create, summary.Create, RaiseRowAggregates);
        Select = new PrivilegeCellViewModel(GrantPrivilege.Select, summary.Select, RaiseRowAggregates);
        Insert = new PrivilegeCellViewModel(GrantPrivilege.Insert, summary.Insert, RaiseRowAggregates);
        Update = new PrivilegeCellViewModel(GrantPrivilege.Update, summary.Update, RaiseRowAggregates);
        Delete = new PrivilegeCellViewModel(GrantPrivilege.Delete, summary.Delete, RaiseRowAggregates);

        Cells = new[] { Usage, Create, Select, Insert, Update, Delete };

        ToggleAllCommand = new RelayCommand(ToggleAll);
    }

    public string SchemaName { get; }
    public PrivilegeCellViewModel Usage { get; }
    public PrivilegeCellViewModel Create { get; }
    public PrivilegeCellViewModel Select { get; }
    public PrivilegeCellViewModel Insert { get; }
    public PrivilegeCellViewModel Update { get; }
    public PrivilegeCellViewModel Delete { get; }

    public IReadOnlyList<PrivilegeCellViewModel> Cells { get; }

    public RelayCommand ToggleAllCommand { get; }

    /// <summary>True when every cell in the row is currently granted (after pending applied).</summary>
    public bool AllGranted => Cells.All(c => c.PendingValue);

    /// <summary>Number of pending changes in this row.</summary>
    public int PendingCount => Cells.Count(c => c.IsDirty);

    public bool HasPending => PendingCount > 0;

    /// <summary>
    /// Flip every cell to the opposite of "all granted". If anything was off, turn it all on;
    /// if everything was on, turn it all off. Matches the "ALL" column behaviour from the mockup.
    /// </summary>
    public void ToggleAll()
    {
        var targetAllOn = !AllGranted;
        foreach (var cell in Cells)
        {
            if (cell.PendingValue != targetAllOn)
                cell.Toggle();
        }
    }

    public void DiscardPending()
    {
        foreach (var cell in Cells)
            cell.DiscardPending();
    }

    public void RebaseFrom(SchemaGrantSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        Usage.RebaseFromCurrent(summary.Usage);
        Create.RebaseFromCurrent(summary.Create);
        Select.RebaseFromCurrent(summary.Select);
        Insert.RebaseFromCurrent(summary.Insert);
        Update.RebaseFromCurrent(summary.Update);
        Delete.RebaseFromCurrent(summary.Delete);
        RaiseRowAggregates();
    }

    /// <summary>
    /// Project this row's pending changes into GrantChange records suitable for ApplyAsync.
    /// Skipped if the row has nothing dirty.
    /// </summary>
    public IEnumerable<GrantChange> EnumeratePendingChanges()
    {
        foreach (var cell in Cells)
        {
            if (!cell.IsDirty)
                continue;
            yield return new GrantChange(
                SchemaName,
                cell.Privilege,
                cell.PendingValue ? GrantOperation.Grant : GrantOperation.Revoke);
        }
    }

    private void RaiseRowAggregates()
    {
        RaisePropertyChanged(nameof(AllGranted));
        RaisePropertyChanged(nameof(PendingCount));
        RaisePropertyChanged(nameof(HasPending));
        _onChanged();
    }
}
