using System.Globalization;
using Wartownik.Connections;
using Wartownik.Dialogs;
using Wartownik.Localization;
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

    // ---------- Confirm-before-destructive-Apply gate ----------

    private static readonly CultureInfo English = new("en");

    private static ConnectionProfile SampleProfile() =>
        ConnectionProfile.Create("Local", "localhost", 5432, "postgres", "alice", PostgresSslMode.Disable);

    /// <summary>
    /// Build a matrix VM wired with fakes, loaded for a single login role "alice" against a
    /// database whose "app" schema carries <paramref name="appGrants"/>. The fakes complete
    /// synchronously so Rows are populated by the time the awaited loads return.
    /// </summary>
    private static async Task<(PermissionsMatrixViewModel vm, FakeGrantService grants, FakeConfirmation confirm)>
        BuildLoadedAsync(SchemaGrantSummary appGrants, bool confirmResult)
    {
        var loc = new LocalizationService(new EmptyResources(), new[] { English }, English);
        var profiles = new FakeProfileService();
        var metadata = new FakeMetadataService(new[]
        {
            new RoleSummary("alice", IsSuperuser: false, CanCreateDb: false, CanCreateRole: false, CanLogin: true),
        });
        var grants = new FakeGrantService(new[] { appGrants });
        var confirm = new FakeConfirmation { NextResult = confirmResult };

        var vm = new PermissionsMatrixViewModel(
            SampleProfile(), "mydb", loc, profiles, metadata, grants,
            previewSqlDialog: null, auditLog: null, confirmation: confirm);

        await vm.LoadAsync();
        await vm.LoadGrantsForSelectedRoleAsync();
        return (vm, grants, confirm);
    }

    [Fact]
    public async Task ApplyAsync_with_revoke_and_confirmation_declined_applies_nothing()
    {
        // "app".Select granted → toggle off = a pending REVOKE.
        var (vm, grants, confirm) = await BuildLoadedAsync(
            new SchemaGrantSummary("app", Usage: false, Create: false, Select: true, Insert: false, Update: false, Delete: false),
            confirmResult: false);
        vm.Rows.Single(r => r.SchemaName == "app").Select.Toggle();

        await vm.ApplyAsync();

        Assert.True(confirm.WasAsked);
        Assert.True(confirm.LastRequest!.IsDestructive);
        Assert.Empty(grants.AppliedRoles); // aborted before touching the database
    }

    [Fact]
    public async Task ApplyAsync_with_revoke_and_confirmation_accepted_applies()
    {
        var (vm, grants, confirm) = await BuildLoadedAsync(
            new SchemaGrantSummary("app", Usage: false, Create: false, Select: true, Insert: false, Update: false, Delete: false),
            confirmResult: true);
        vm.Rows.Single(r => r.SchemaName == "app").Select.Toggle();

        await vm.ApplyAsync();

        Assert.True(confirm.WasAsked);
        Assert.Single(grants.AppliedRoles);
        Assert.Equal("alice", grants.AppliedRoles[0]);
    }

    [Fact]
    public async Task ApplyAsync_grants_only_batch_skips_confirmation()
    {
        // "app" fully ungranted → toggle Select on = a pending GRANT, no revokes.
        var (vm, grants, confirm) = await BuildLoadedAsync(
            new SchemaGrantSummary("app", Usage: false, Create: false, Select: false, Insert: false, Update: false, Delete: false),
            confirmResult: false); // would decline IF asked — but a grants-only batch must not ask
        vm.Rows.Single(r => r.SchemaName == "app").Select.Toggle();

        await vm.ApplyAsync();

        Assert.False(confirm.WasAsked);
        Assert.Single(grants.AppliedRoles); // applied even though NextResult was false, because never prompted
    }

    [Fact]
    public async Task ApplyRoleAsync_with_revoke_and_confirmation_declined_applies_nothing()
    {
        var (vm, grants, confirm) = await BuildLoadedAsync(
            new SchemaGrantSummary("app", Usage: false, Create: false, Select: true, Insert: false, Update: false, Delete: false),
            confirmResult: false);
        vm.Rows.Single(r => r.SchemaName == "app").Select.Toggle();

        await vm.ApplyRoleAsync("alice");

        Assert.True(confirm.WasAsked);
        Assert.Empty(grants.AppliedRoles);
    }

    // ---------- Selective apply (per-change checkboxes) ----------

    [Fact]
    public async Task ApplySelectedAsync_applies_only_ticked_changes_and_keeps_the_rest_pending()
    {
        var (vm, grants, _) = await BuildLoadedAsync(
            new SchemaGrantSummary("app", Usage: false, Create: false, Select: false, Insert: false, Update: false, Delete: false),
            confirmResult: true);
        var row = vm.Rows.Single(r => r.SchemaName == "app");
        row.Select.Toggle(); // pending grant
        row.Insert.Toggle(); // pending grant
        Assert.Equal(2, vm.PendingCount);

        // Un-tick the Insert change in the sticky bar.
        vm.PendingGroups.Single().Changes.Single(c => c.Privilege == GrantPrivilege.Insert).IsSelected = false;
        Assert.Equal(1, vm.SelectedChangeCount);

        await vm.ApplySelectedAsync();

        // Only Select went through.
        Assert.Single(grants.AppliedChanges);
        Assert.Equal(GrantPrivilege.Select, grants.AppliedChanges[0].Privilege);
        Assert.Equal(GrantOperation.Grant, grants.AppliedChanges[0].Operation);
        // Insert stayed pending; the de-selection was cleared so it's selected again.
        Assert.Equal(1, vm.PendingCount);
        Assert.Equal(1, vm.SelectedChangeCount);
    }

    [Fact]
    public async Task ApplySelectedAsync_deselecting_the_only_revoke_skips_confirmation()
    {
        var (vm, grants, confirm) = await BuildLoadedAsync(
            new SchemaGrantSummary("app", Usage: false, Create: false, Select: true, Insert: false, Update: false, Delete: false),
            confirmResult: false); // would decline a revoke IF asked
        var row = vm.Rows.Single(r => r.SchemaName == "app");
        row.Select.Toggle(); // granted → pending REVOKE
        row.Insert.Toggle(); // → pending grant

        // Un-tick the revoke, leaving only the grant selected.
        vm.PendingGroups.Single().Changes.Single(c => c.Privilege == GrantPrivilege.Select).IsSelected = false;

        await vm.ApplySelectedAsync();

        Assert.False(confirm.WasAsked); // grants-only selection → no destructive prompt
        Assert.Single(grants.AppliedChanges);
        Assert.Equal(GrantPrivilege.Insert, grants.AppliedChanges[0].Privilege);
    }

    [Fact]
    public async Task ApplySelectedAsync_with_selected_revoke_declined_applies_nothing()
    {
        var (vm, grants, confirm) = await BuildLoadedAsync(
            new SchemaGrantSummary("app", Usage: false, Create: false, Select: true, Insert: false, Update: false, Delete: false),
            confirmResult: false);
        vm.Rows.Single(r => r.SchemaName == "app").Select.Toggle(); // pending revoke, selected

        await vm.ApplySelectedAsync();

        Assert.True(confirm.WasAsked);
        Assert.Empty(grants.AppliedChanges);
        Assert.Equal(1, vm.PendingCount); // change stays pending
    }

    [Fact]
    public async Task Revisiting_a_role_with_cached_pending_does_not_double_count()
    {
        var loc = new LocalizationService(new EmptyResources(), new[] { English }, English);
        var metadata = new FakeMetadataService(new[]
        {
            new RoleSummary("alice", IsSuperuser: false, CanCreateDb: false, CanCreateRole: false, CanLogin: true),
            new RoleSummary("bob", IsSuperuser: false, CanCreateDb: false, CanCreateRole: false, CanLogin: true),
        });
        var grants = new FakeGrantService(new[]
        {
            new SchemaGrantSummary("app", false, false, false, false, false, false),
        });
        var vm = new PermissionsMatrixViewModel(
            SampleProfile(), "mydb", loc, new FakeProfileService(), metadata, grants);

        await vm.LoadAsync(); // alice selected + loaded (synchronously via the setter)
        vm.Rows.Single(r => r.SchemaName == "app").Select.Toggle(); // alice: 1 pending

        vm.SelectedRole = vm.TargetRoles.Single(r => r.Name == "bob"); // caches alice, loads bob
        vm.Rows.Single(r => r.SchemaName == "app").Insert.Toggle(); // bob: 1 pending

        vm.SelectedRole = vm.TargetRoles.Single(r => r.Name == "alice"); // caches bob, restores alice

        // alice restored to live rows, bob cached — 2 total, NOT 3 (the invariant fix stops the
        // selected role being counted in both the live rows and the cache).
        Assert.Equal(2, vm.PendingCount);
        Assert.Equal(2, vm.PendingRoleCount);
    }

    // ---------- Fakes ----------

    private sealed class EmptyResources : IStringResources
    {
        public string? Get(string key, CultureInfo culture) => null;
    }

    private sealed class FakeProfileService : IConnectionProfileService
    {
        public Task<IReadOnlyList<ConnectionProfile>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConnectionProfile>>(Array.Empty<ConnectionProfile>());
        public Task<ConnectionProfile?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<ConnectionProfile?>(null);
        public Task<string?> GetPasswordAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>("pw");
        public Task SaveAsync(ConnectionProfile profile, string password, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class FakeMetadataService : IPostgresMetadataService
    {
        private readonly IReadOnlyList<RoleSummary> _roles;
        public FakeMetadataService(IReadOnlyList<RoleSummary> roles) => _roles = roles;

        public Task<IReadOnlyList<DatabaseSummary>> ListDatabasesAsync(
            ConnectionProfile profile, string password, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DatabaseSummary>>(Array.Empty<DatabaseSummary>());
        public Task<IReadOnlyList<RoleSummary>> ListRolesAsync(
            ConnectionProfile profile, string password, CancellationToken cancellationToken = default) =>
            Task.FromResult(_roles);
        public Task<IReadOnlyList<SchemaSummary>> ListSchemasAsync(
            ConnectionProfile profile, string password, string databaseName, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SchemaSummary>>(Array.Empty<SchemaSummary>());
    }

    private sealed class FakeGrantService : IPostgresGrantService
    {
        private readonly IReadOnlyList<SchemaGrantSummary> _grants;
        public List<string> AppliedRoles { get; } = new();
        public List<GrantChange> AppliedChanges { get; } = new();

        public FakeGrantService(IReadOnlyList<SchemaGrantSummary> grants) => _grants = grants;

        public Task<IReadOnlyList<SchemaGrantSummary>> ListSchemaGrantsAsync(
            ConnectionProfile profile, string profilePassword, string databaseName, string roleName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_grants);

        public Task ApplyGrantsAsync(
            ConnectionProfile profile, string profilePassword, string databaseName, string roleName,
            IReadOnlyList<GrantChange> changes, CancellationToken cancellationToken = default)
        {
            AppliedRoles.Add(roleName);
            AppliedChanges.AddRange(changes);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeConfirmation : IConfirmationDialog
    {
        public bool NextResult { get; set; }
        public bool WasAsked { get; private set; }
        public ConfirmationRequest? LastRequest { get; private set; }

        public Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken cancellationToken = default)
        {
            WasAsked = true;
            LastRequest = request;
            return Task.FromResult(NextResult);
        }
    }
}
