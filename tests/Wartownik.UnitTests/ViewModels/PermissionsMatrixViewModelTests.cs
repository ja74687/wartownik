using Wartownik.Connections;
using Wartownik.ViewModels;

namespace Wartownik.UnitTests.ViewModels;

public class PermissionsMatrixViewModelTests
{
    // ---------- PrivilegeCellViewModel ----------

    [Fact]
    public void Cell_starts_in_granted_state_when_initial_value_true()
    {
        var fired = false;
        var cell = new PrivilegeCellViewModel(GrantPrivilege.Select, initialValue: true, () => fired = true);
        Assert.Equal(CellState.Granted, cell.State);
        Assert.False(cell.IsDirty);
        Assert.False(fired);
    }

    [Fact]
    public void Cell_toggle_from_granted_moves_to_pending_revoke_and_signals_change()
    {
        var fired = 0;
        var cell = new PrivilegeCellViewModel(GrantPrivilege.Select, initialValue: true, () => fired++);
        cell.Toggle();
        Assert.Equal(CellState.PendingRevoke, cell.State);
        Assert.True(cell.IsDirty);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Cell_toggle_from_not_granted_moves_to_pending_grant()
    {
        var cell = new PrivilegeCellViewModel(GrantPrivilege.Insert, initialValue: false, () => { });
        cell.Toggle();
        Assert.Equal(CellState.PendingGrant, cell.State);
        Assert.True(cell.IsDirty);
    }

    [Fact]
    public void Cell_double_toggle_returns_to_baseline_and_clears_dirty()
    {
        var cell = new PrivilegeCellViewModel(GrantPrivilege.Select, initialValue: true, () => { });
        cell.Toggle();
        cell.Toggle();
        Assert.Equal(CellState.Granted, cell.State);
        Assert.False(cell.IsDirty);
    }

    [Fact]
    public void Cell_discard_reverts_pending_to_current()
    {
        var cell = new PrivilegeCellViewModel(GrantPrivilege.Select, initialValue: false, () => { });
        cell.Toggle();
        Assert.True(cell.IsDirty);
        cell.DiscardPending();
        Assert.False(cell.IsDirty);
        Assert.Equal(CellState.NotGranted, cell.State);
    }

    [Fact]
    public void Cell_rebase_adopts_new_baseline_and_clears_dirty()
    {
        var cell = new PrivilegeCellViewModel(GrantPrivilege.Select, initialValue: false, () => { });
        cell.Toggle(); // pending grant
        cell.RebaseFromCurrent(true);
        // After rebase, what we wanted (true) is now the actual state (true) — so no dirty.
        Assert.False(cell.IsDirty);
        Assert.Equal(CellState.Granted, cell.State);
    }

    // ---------- SchemaPermissionRowViewModel ----------

    private static SchemaGrantSummary AllOff(string schema = "app") =>
        new(schema, false, false, false, false, false, false);

    private static SchemaGrantSummary AllOn(string schema = "app") =>
        new(schema, true, true, true, true, true, true);

    [Fact]
    public void Row_toggle_all_when_off_grants_every_cell()
    {
        var row = new SchemaPermissionRowViewModel(AllOff(), () => { });
        row.ToggleAll();
        Assert.True(row.AllGranted);
        Assert.Equal(6, row.PendingCount);
        Assert.True(row.HasPending);
    }

    [Fact]
    public void Row_toggle_all_when_fully_granted_revokes_every_cell()
    {
        var row = new SchemaPermissionRowViewModel(AllOn(), () => { });
        row.ToggleAll();
        Assert.False(row.AllGranted);
        Assert.Equal(6, row.PendingCount);
    }

    [Fact]
    public void Row_enumerate_pending_only_emits_dirty_cells_with_correct_operation()
    {
        var row = new SchemaPermissionRowViewModel(
            new SchemaGrantSummary("app", Usage: true, Create: false, Select: true, Insert: false, Update: false, Delete: false),
            () => { });

        row.Usage.Toggle();   // true -> false: Revoke
        row.Insert.Toggle();  // false -> true: Grant

        var changes = row.EnumeratePendingChanges().ToList();

        Assert.Equal(2, changes.Count);
        Assert.Contains(changes, c => c.Privilege == GrantPrivilege.Usage && c.Operation == GrantOperation.Revoke);
        Assert.Contains(changes, c => c.Privilege == GrantPrivilege.Insert && c.Operation == GrantOperation.Grant);
    }

    [Fact]
    public void Row_discard_clears_all_pending_changes()
    {
        var row = new SchemaPermissionRowViewModel(AllOff(), () => { });
        row.Select.Toggle();
        row.Insert.Toggle();
        Assert.Equal(2, row.PendingCount);

        row.DiscardPending();

        Assert.Equal(0, row.PendingCount);
        Assert.Empty(row.EnumeratePendingChanges());
    }

    [Fact]
    public void Row_rebase_resets_dirty_state_to_new_database_truth()
    {
        var row = new SchemaPermissionRowViewModel(AllOff(), () => { });
        row.Select.Toggle();
        Assert.Equal(1, row.PendingCount);

        row.RebaseFrom(AllOn());

        Assert.Equal(0, row.PendingCount);
        Assert.True(row.AllGranted);
    }

    // ---------- Aggregates roll up to parent ----------

    [Fact]
    public void Row_change_callback_fires_on_cell_toggle()
    {
        var notifications = 0;
        var row = new SchemaPermissionRowViewModel(AllOff(), () => notifications++);
        row.Select.Toggle();
        Assert.Equal(1, notifications);
    }
}
