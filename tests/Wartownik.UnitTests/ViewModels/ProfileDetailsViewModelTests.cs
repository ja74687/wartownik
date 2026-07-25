using System.Globalization;
using Wartownik.Connections;
using Wartownik.Dialogs;
using Wartownik.Localization;
using Wartownik.ViewModels;

namespace Wartownik.UnitTests.ViewModels;

public class ProfileDetailsViewModelTests
{
    private static readonly CultureInfo English = new("en");

    private static ConnectionProfile SampleProfile() =>
        ConnectionProfile.Create(
            displayName: "Local",
            host: "localhost",
            port: 5432,
            database: "postgres",
            username: "alice",
            sslMode: PostgresSslMode.Disable);

    private static ProfileDetailsViewModel Create(
        FakeProfileService? profiles = null,
        FakeMetadataService? metadata = null,
        FakeRoleAdminService? roleAdmin = null,
        FakeRoleEditor? roleEditor = null,
        FakeConfirmationDialog? confirmation = null,
        FakeMembershipService? membership = null,
        FakeMembershipEditor? membershipEditor = null)
    {
        var loc = new LocalizationService(
            new EmptyResources(),
            new[] { English },
            English);
        var profilesService = profiles ?? new FakeProfileService();
        var metaService = metadata ?? new FakeMetadataService();
        ProfileDetailsViewModel.DatabaseDetailsFactory dbFactory = (p, db) =>
            new DatabaseDetailsViewModel(p, db, loc, profilesService, metaService);

        return new ProfileDetailsViewModel(
            SampleProfile(),
            loc,
            profilesService,
            metaService,
            roleAdmin ?? new FakeRoleAdminService(),
            roleEditor ?? new FakeRoleEditor(),
            confirmation ?? new FakeConfirmationDialog { NextResult = true },
            dbFactory,
            membership,
            membershipEditor);
    }

    [Fact]
    public async Task LoadAsync_populates_databases_and_clears_loading_state()
    {
        var metadata = new FakeMetadataService(new[] { "alpha", "beta", "gamma" });
        var sut = Create(metadata: metadata);

        await sut.LoadAsync();

        Assert.False(sut.IsLoading);
        Assert.Null(sut.ErrorMessage);
        Assert.Equal(3, sut.Databases.Count);
        Assert.Equal(new[] { "alpha", "beta", "gamma" }, sut.Databases.Select(d => d.Name));
        Assert.True(sut.HasDatabases);
        Assert.False(sut.IsDatabasesEmpty);
    }

    [Fact]
    public async Task LoadAsync_marks_empty_when_no_databases_returned()
    {
        var metadata = new FakeMetadataService(Array.Empty<string>());
        var sut = Create(metadata: metadata);

        await sut.LoadAsync();

        Assert.False(sut.HasDatabases);
        Assert.True(sut.IsDatabasesEmpty);
        Assert.True(sut.IsRolesEmpty);
    }

    [Fact]
    public async Task LoadAsync_sets_error_message_when_metadata_throws()
    {
        var metadata = new FakeMetadataService(_ => throw new InvalidOperationException("boom"));
        var sut = Create(metadata: metadata);

        await sut.LoadAsync();

        Assert.False(sut.IsLoading);
        Assert.Equal("boom", sut.ErrorMessage);
        Assert.True(sut.HasError);
        Assert.Empty(sut.Databases);
    }

    [Fact]
    public async Task LoadAsync_passes_password_from_profile_service_to_metadata()
    {
        var profiles = new FakeProfileService();
        var loc = new LocalizationService(new EmptyResources(), new[] { English }, English);
        var profile = SampleProfile();
        profiles.SavedPasswords[profile.Id] = "secret123";

        var metadata = new FakeMetadataService(new[] { "x" });
        ProfileDetailsViewModel.DatabaseDetailsFactory dbFactory = (p, db) =>
            new DatabaseDetailsViewModel(p, db, loc, profiles, metadata);
        var sut = new ProfileDetailsViewModel(profile, loc, profiles, metadata,
            new FakeRoleAdminService(), new FakeRoleEditor(),
            new FakeConfirmationDialog { NextResult = true }, dbFactory);

        await sut.LoadAsync();

        Assert.Equal("secret123", metadata.LastPassword);
        Assert.Equal(profile.Id, metadata.LastProfile?.Id);
    }

    [Fact]
    public async Task LoadAsync_uses_empty_string_password_when_credential_not_found()
    {
        var profiles = new FakeProfileService(); // no passwords saved
        var metadata = new FakeMetadataService(new[] { "x" });
        var sut = Create(profiles: profiles, metadata: metadata);

        await sut.LoadAsync();

        Assert.Equal("", metadata.LastPassword);
    }

    [Fact]
    public async Task LoadAsync_splits_login_roles_into_Users_and_no_login_into_Roles()
    {
        var metadata = new FakeMetadataService(new[] { "ignored" });
        metadata.SetRoles(new[]
        {
            new RoleSummary("admin", IsSuperuser: true, CanCreateDb: true, CanCreateRole: true, CanLogin: true),
            new RoleSummary("readonly", IsSuperuser: false, CanCreateDb: false, CanCreateRole: false, CanLogin: true),
            new RoleSummary("group_a", IsSuperuser: false, CanCreateDb: false, CanCreateRole: false, CanLogin: false),
        });
        var sut = Create(metadata: metadata);

        await sut.LoadAsync();

        Assert.Equal(new[] { "admin", "readonly" }, sut.Users.Select(r => r.Name));
        Assert.Equal(new[] { "group_a" }, sut.Roles.Select(r => r.Name));
        Assert.True(sut.HasUsers);
        Assert.True(sut.HasRoles);
        Assert.False(sut.IsUsersEmpty);
        Assert.False(sut.IsRolesEmpty);
    }

    [Fact]
    public async Task LoadAsync_marks_users_and_roles_empty_when_none_returned()
    {
        var metadata = new FakeMetadataService(new[] { "db1" });
        // SetRoles defaults to empty
        var sut = Create(metadata: metadata);

        await sut.LoadAsync();

        Assert.False(sut.HasUsers);
        Assert.True(sut.IsUsersEmpty);
        Assert.False(sut.HasRoles);
        Assert.True(sut.IsRolesEmpty);
    }

    [Fact]
    public async Task LoadAsync_marks_users_empty_when_only_no_login_roles_returned()
    {
        var metadata = new FakeMetadataService(new[] { "db1" });
        metadata.SetRoles(new[]
        {
            new RoleSummary("group_a", IsSuperuser: false, CanCreateDb: false, CanCreateRole: false, CanLogin: false),
        });
        var sut = Create(metadata: metadata);

        await sut.LoadAsync();

        Assert.True(sut.IsUsersEmpty);
        Assert.False(sut.IsRolesEmpty);
    }

    [Fact]
    public async Task AddUserCommand_passes_canLoginDefault_true_to_editor()
    {
        var roleEditor = new FakeRoleEditor { NextRequest = null };
        var sut = Create(roleEditor: roleEditor);

        await sut.AddUserCommand.ExecuteAsync();

        Assert.True(roleEditor.LastCanLoginDefault);
    }

    [Fact]
    public async Task AddRoleCommand_passes_canLoginDefault_false_to_editor()
    {
        var roleEditor = new FakeRoleEditor { NextRequest = null };
        var sut = Create(roleEditor: roleEditor);

        await sut.AddRoleCommand.ExecuteAsync();

        Assert.False(roleEditor.LastCanLoginDefault);
    }

    [Fact]
    public async Task LoadAsync_replaces_previous_state_on_subsequent_calls()
    {
        var metadata = new FakeMetadataService(new[] { "first" });
        var sut = Create(metadata: metadata);
        await sut.LoadAsync();

        metadata.SetResult(new[] { "second", "third" });
        await sut.LoadAsync();

        Assert.Equal(2, sut.Databases.Count);
        Assert.Equal(new[] { "second", "third" }, sut.Databases.Select(d => d.Name));
    }

    [Fact]
    public void Endpoint_combines_profile_fields()
    {
        var sut = Create();

        Assert.Equal("localhost:5432 / postgres / alice", sut.Endpoint);
    }

    [Fact]
    public async Task AddRoleCommand_when_editor_returns_request_creates_role_and_reloads()
    {
        var profiles = new FakeProfileService();
        var profile = SampleProfile();
        profiles.SavedPasswords[profile.Id] = "pwd";

        var metadata = new FakeMetadataService(Array.Empty<string>());
        var roleAdmin = new FakeRoleAdminService();
        var roleEditor = new FakeRoleEditor
        {
            NextRequest = new CreateRoleRequest("alice", false, false, false, true, "rolepw"),
        };
        var loc = new LocalizationService(new EmptyResources(), new[] { English }, English);
        ProfileDetailsViewModel.DatabaseDetailsFactory dbFactory = (p, db) =>
            new DatabaseDetailsViewModel(p, db, loc, profiles, metadata);
        var sut = new ProfileDetailsViewModel(profile, loc, profiles, metadata, roleAdmin, roleEditor,
            new FakeConfirmationDialog { NextResult = true }, dbFactory);

        await sut.AddRoleCommand.ExecuteAsync();

        Assert.Single(roleAdmin.CreatedRoles);
        Assert.Equal("alice", roleAdmin.CreatedRoles[0].RoleName);
        Assert.Equal("pwd", roleAdmin.LastPassword);
    }

    [Fact]
    public async Task AddRoleCommand_when_editor_cancels_does_not_call_admin()
    {
        var roleAdmin = new FakeRoleAdminService();
        var roleEditor = new FakeRoleEditor { NextRequest = null };
        var sut = Create(roleAdmin: roleAdmin, roleEditor: roleEditor);

        await sut.AddRoleCommand.ExecuteAsync();

        Assert.Empty(roleAdmin.CreatedRoles);
    }

    [Fact]
    public async Task AddRoleCommand_sets_error_message_when_admin_throws()
    {
        var roleAdmin = new FakeRoleAdminService { ThrowOnCreate = new InvalidOperationException("boom") };
        var roleEditor = new FakeRoleEditor
        {
            NextRequest = new CreateRoleRequest("x", false, false, false, false, null),
        };
        var sut = Create(roleAdmin: roleAdmin, roleEditor: roleEditor);

        await sut.AddRoleCommand.ExecuteAsync();

        Assert.Equal("boom", sut.ErrorMessage);
    }

    [Fact]
    public async Task EditRoleCommand_when_editor_returns_alter_request_calls_admin_and_reloads()
    {
        var profiles = new FakeProfileService();
        var profile = SampleProfile();
        profiles.SavedPasswords[profile.Id] = "pwd";

        var roleAdmin = new FakeRoleAdminService();
        var roleEditor = new FakeRoleEditor
        {
            NextAlterRequest = new AlterRoleRequest("alice", true, false, false, true, "newpw"),
        };
        var loc = new LocalizationService(new EmptyResources(), new[] { English }, English);
        var meta = new FakeMetadataService(Array.Empty<string>());
        ProfileDetailsViewModel.DatabaseDetailsFactory dbFactory = (p, db) =>
            new DatabaseDetailsViewModel(p, db, loc, profiles, meta);
        var sut = new ProfileDetailsViewModel(profile, loc, profiles, meta,
            roleAdmin, roleEditor, new FakeConfirmationDialog(), dbFactory);
        var item = new RoleItemViewModel(new RoleSummary("alice", false, false, false, true), loc);

        await sut.EditRoleCommand.ExecuteAsync(item);

        Assert.Equal("alice", roleEditor.LastEditTarget!.Name);
        Assert.Single(roleAdmin.AlteredRoles);
        Assert.Equal("alice", roleAdmin.AlteredRoles[0].RoleName);
        Assert.True(roleAdmin.AlteredRoles[0].IsSuperuser);
        Assert.Equal("pwd", roleAdmin.LastAlterPassword);
    }

    [Fact]
    public async Task EditRoleCommand_when_editor_cancels_does_not_call_admin()
    {
        var roleAdmin = new FakeRoleAdminService();
        var roleEditor = new FakeRoleEditor { NextAlterRequest = null };
        var sut = Create(roleAdmin: roleAdmin, roleEditor: roleEditor);
        var item = new RoleItemViewModel(new RoleSummary("x", false, false, false, false), sut.Localization);

        await sut.EditRoleCommand.ExecuteAsync(item);

        Assert.Empty(roleAdmin.AlteredRoles);
    }

    [Fact]
    public async Task EditRoleCommand_sets_error_message_when_admin_throws()
    {
        var roleAdmin = new FakeRoleAdminService { ThrowOnAlter = new InvalidOperationException("permission denied") };
        var roleEditor = new FakeRoleEditor
        {
            NextAlterRequest = new AlterRoleRequest("x", false, false, false, false, null),
        };
        var sut = Create(roleAdmin: roleAdmin, roleEditor: roleEditor);
        var item = new RoleItemViewModel(new RoleSummary("x", false, false, false, false), sut.Localization);

        await sut.EditRoleCommand.ExecuteAsync(item);

        Assert.Equal("permission denied", sut.ErrorMessage);
    }

    [Fact]
    public async Task EditRoleCommand_with_invalid_parameter_does_nothing()
    {
        var roleAdmin = new FakeRoleAdminService();
        var roleEditor = new FakeRoleEditor
        {
            NextAlterRequest = new AlterRoleRequest("x", false, false, false, false, null),
        };
        var sut = Create(roleAdmin: roleAdmin, roleEditor: roleEditor);

        await sut.EditRoleCommand.ExecuteAsync("not a role");

        Assert.Null(roleEditor.LastEditTarget);
        Assert.Empty(roleAdmin.AlteredRoles);
    }

    [Fact]
    public async Task DropRoleCommand_when_user_confirms_drops_role_and_reloads()
    {
        var profiles = new FakeProfileService();
        var profile = SampleProfile();
        profiles.SavedPasswords[profile.Id] = "pwd";
        var metadata = new FakeMetadataService(Array.Empty<string>());
        var roleAdmin = new FakeRoleAdminService();
        var confirmation = new FakeConfirmationDialog { NextResult = true };
        var loc = new LocalizationService(new EmptyResources(), new[] { English }, English);

        ProfileDetailsViewModel.DatabaseDetailsFactory dbFactory = (p, db) =>
            new DatabaseDetailsViewModel(p, db, loc, profiles, metadata);
        var sut = new ProfileDetailsViewModel(profile, loc, profiles, metadata, roleAdmin,
            new FakeRoleEditor(), confirmation, dbFactory);
        var item = new RoleItemViewModel(new RoleSummary("alice", false, false, false, true), loc);

        await sut.DropRoleCommand.ExecuteAsync(item);

        Assert.True(confirmation.WasAsked);
        Assert.True(confirmation.LastRequest!.IsDestructive);
        Assert.Single(roleAdmin.DroppedRoles);
        Assert.Equal("alice", roleAdmin.DroppedRoles[0]);
        Assert.Equal("pwd", roleAdmin.LastDropPassword);
    }

    [Fact]
    public async Task DropRoleCommand_when_user_cancels_does_not_drop()
    {
        var roleAdmin = new FakeRoleAdminService();
        var confirmation = new FakeConfirmationDialog { NextResult = false };
        var sut = Create(roleAdmin: roleAdmin, confirmation: confirmation);
        var loc = sut.Localization;
        var item = new RoleItemViewModel(new RoleSummary("x", false, false, false, false), loc);

        await sut.DropRoleCommand.ExecuteAsync(item);

        Assert.True(confirmation.WasAsked);
        Assert.Empty(roleAdmin.DroppedRoles);
    }

    [Fact]
    public async Task DropRoleCommand_with_invalid_parameter_does_nothing()
    {
        var roleAdmin = new FakeRoleAdminService();
        var confirmation = new FakeConfirmationDialog { NextResult = true };
        var sut = Create(roleAdmin: roleAdmin, confirmation: confirmation);

        await sut.DropRoleCommand.ExecuteAsync("not a role");

        Assert.False(confirmation.WasAsked);
        Assert.Empty(roleAdmin.DroppedRoles);
    }

    [Fact]
    public async Task DropRoleCommand_sets_error_message_when_admin_throws()
    {
        var roleAdmin = new FakeRoleAdminService { ThrowOnDrop = new InvalidOperationException("owns objects") };
        var confirmation = new FakeConfirmationDialog { NextResult = true };
        var sut = Create(roleAdmin: roleAdmin, confirmation: confirmation);
        var item = new RoleItemViewModel(new RoleSummary("x", false, false, false, false), sut.Localization);

        await sut.DropRoleCommand.ExecuteAsync(item);

        Assert.Equal("owns objects", sut.ErrorMessage);
    }

    [Fact]
    public async Task OpenDatabaseCommand_sets_selected_database_and_loads_it()
    {
        var sut = Create(metadata: new FakeMetadataService(new[] { "ignored" }));
        var item = new DatabaseItemViewModel(new DatabaseSummary("mydb"));

        await sut.OpenDatabaseCommand.ExecuteAsync(item);

        Assert.NotNull(sut.SelectedDatabase);
        Assert.True(sut.IsViewingDatabase);
        Assert.Equal("mydb", sut.SelectedDatabase!.DatabaseName);
    }

    [Fact]
    public async Task OpenDatabaseCommand_with_invalid_parameter_does_nothing()
    {
        var sut = Create();

        await sut.OpenDatabaseCommand.ExecuteAsync("not a viewmodel");

        Assert.Null(sut.SelectedDatabase);
        Assert.False(sut.IsViewingDatabase);
    }

    [Fact]
    public async Task BackToDatabasesCommand_clears_selected_database()
    {
        var sut = Create();
        var item = new DatabaseItemViewModel(new DatabaseSummary("mydb"));
        await sut.OpenDatabaseCommand.ExecuteAsync(item);
        Assert.True(sut.IsViewingDatabase);

        await sut.BackToDatabasesCommand.ExecuteAsync();

        Assert.Null(sut.SelectedDatabase);
        Assert.False(sut.IsViewingDatabase);
    }

    [Fact]
    public void Constructor_throws_on_null_arguments()
    {
        var loc = new LocalizationService(new EmptyResources(), new[] { English }, English);
        var profile = SampleProfile();
        var profiles = new FakeProfileService();
        var metadata = new FakeMetadataService();
        var roleAdmin = new FakeRoleAdminService();
        var roleEditor = new FakeRoleEditor();
        var confirm = new FakeConfirmationDialog();
        ProfileDetailsViewModel.DatabaseDetailsFactory dbFactory = (p, db) =>
            new DatabaseDetailsViewModel(p, db, loc, profiles, metadata);

        Assert.Throws<ArgumentNullException>(() => new ProfileDetailsViewModel(null!, loc, profiles, metadata, roleAdmin, roleEditor, confirm, dbFactory));
        Assert.Throws<ArgumentNullException>(() => new ProfileDetailsViewModel(profile, null!, profiles, metadata, roleAdmin, roleEditor, confirm, dbFactory));
        Assert.Throws<ArgumentNullException>(() => new ProfileDetailsViewModel(profile, loc, null!, metadata, roleAdmin, roleEditor, confirm, dbFactory));
        Assert.Throws<ArgumentNullException>(() => new ProfileDetailsViewModel(profile, loc, profiles, null!, roleAdmin, roleEditor, confirm, dbFactory));
        Assert.Throws<ArgumentNullException>(() => new ProfileDetailsViewModel(profile, loc, profiles, metadata, null!, roleEditor, confirm, dbFactory));
        Assert.Throws<ArgumentNullException>(() => new ProfileDetailsViewModel(profile, loc, profiles, metadata, roleAdmin, null!, confirm, dbFactory));
        Assert.Throws<ArgumentNullException>(() => new ProfileDetailsViewModel(profile, loc, profiles, metadata, roleAdmin, roleEditor, null!, dbFactory));
        Assert.Throws<ArgumentNullException>(() => new ProfileDetailsViewModel(profile, loc, profiles, metadata, roleAdmin, roleEditor, confirm, null!));
    }

    // ---------- Role membership ----------

    private static RoleSummary Role(string name, bool canLogin) =>
        new(name, IsSuperuser: false, CanCreateDb: false, CanCreateRole: false, CanLogin: canLogin);

    [Fact]
    public async Task LoadAsync_attaches_each_roles_groups()
    {
        var meta = new FakeMetadataService();
        meta.SetRoles(new[] { Role("alice", true), Role("devs", false) });
        var membership = new FakeMembershipService
        {
            Edges = { new RoleMembership("alice", "devs") },
        };
        var vm = Create(metadata: meta, membership: membership);

        await vm.LoadAsync();

        Assert.Equal(new[] { "devs" }, vm.Users.Single().MemberOf);
        Assert.True(vm.Users.Single().HasMemberships);
        Assert.False(vm.Roles.Single().HasMemberships); // devs belongs to nothing
    }

    [Fact]
    public async Task LoadAsync_still_lists_roles_when_membership_lookup_fails()
    {
        var meta = new FakeMetadataService();
        meta.SetRoles(new[] { Role("alice", true) });
        var membership = new FakeMembershipService { ThrowOnList = true };
        var vm = Create(metadata: meta, membership: membership);

        await vm.LoadAsync();

        Assert.Single(vm.Users);                       // roles survived
        Assert.False(vm.Users.Single().HasMemberships); // just no membership line
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task EditMembershipCommand_applies_the_confirmed_changes()
    {
        var meta = new FakeMetadataService();
        meta.SetRoles(new[] { Role("alice", true), Role("devs", false) });
        var membership = new FakeMembershipService();
        var editor = new FakeMembershipEditor
        {
            NextResult = new[] { new RoleMembershipChange("devs", GrantOperation.Grant) },
        };
        var vm = Create(metadata: meta, membership: membership, membershipEditor: editor);
        await vm.LoadAsync();

        await vm.EditMembershipCommand.ExecuteAsync(vm.Users.Single());

        Assert.Equal("alice", membership.LastMemberRole);
        var applied = Assert.Single(membership.AppliedChanges);
        Assert.Equal("devs", applied.GroupRole);
        Assert.Equal(GrantOperation.Grant, applied.Operation);
    }

    [Fact]
    public async Task EditMembershipCommand_offers_every_role_as_a_candidate_group()
    {
        var meta = new FakeMetadataService();
        meta.SetRoles(new[] { Role("alice", true), Role("devs", false), Role("ops", false) });
        var membership = new FakeMembershipService
        {
            Edges = { new RoleMembership("alice", "devs") },
        };
        var editor = new FakeMembershipEditor();
        var vm = Create(metadata: meta, membership: membership, membershipEditor: editor);
        await vm.LoadAsync();

        await vm.EditMembershipCommand.ExecuteAsync(vm.Users.Single());

        Assert.Equal(new[] { "alice", "devs", "ops" }, editor.LastAllRoles!.Select(r => r.Name));
        Assert.Equal(new[] { "devs" }, editor.LastCurrentGroups); // the dialog gets today's state
    }

    [Fact]
    public async Task EditMembershipCommand_when_cancelled_applies_nothing()
    {
        var meta = new FakeMetadataService();
        meta.SetRoles(new[] { Role("alice", true) });
        var membership = new FakeMembershipService();
        var editor = new FakeMembershipEditor { NextResult = null }; // cancelled
        var vm = Create(metadata: meta, membership: membership, membershipEditor: editor);
        await vm.LoadAsync();

        await vm.EditMembershipCommand.ExecuteAsync(vm.Users.Single());

        Assert.Empty(membership.AppliedChanges);
    }

    [Fact]
    public async Task EditMembershipCommand_with_an_empty_diff_does_not_hit_the_database()
    {
        var meta = new FakeMetadataService();
        meta.SetRoles(new[] { Role("alice", true) });
        var membership = new FakeMembershipService();
        var editor = new FakeMembershipEditor { NextResult = Array.Empty<RoleMembershipChange>() };
        var vm = Create(metadata: meta, membership: membership, membershipEditor: editor);
        await vm.LoadAsync();

        await vm.EditMembershipCommand.ExecuteAsync(vm.Users.Single());

        Assert.Null(membership.LastMemberRole); // Save with nothing ticked is a no-op
    }

    [Fact]
    public async Task EditMembershipCommand_surfaces_an_apply_failure()
    {
        var meta = new FakeMetadataService();
        meta.SetRoles(new[] { Role("alice", true) });
        var membership = new FakeMembershipService { ThrowOnApply = true };
        var editor = new FakeMembershipEditor
        {
            NextResult = new[] { new RoleMembershipChange("devs", GrantOperation.Grant) },
        };
        var vm = Create(metadata: meta, membership: membership, membershipEditor: editor);
        await vm.LoadAsync();

        await vm.EditMembershipCommand.ExecuteAsync(vm.Users.Single());

        Assert.True(vm.HasError);
    }

    [Fact]
    public void CanEditMembership_is_false_without_the_service_or_the_dialog()
    {
        Assert.False(Create().CanEditMembership);
        Assert.False(Create(membership: new FakeMembershipService()).CanEditMembership);
        Assert.True(Create(
            membership: new FakeMembershipService(),
            membershipEditor: new FakeMembershipEditor()).CanEditMembership);
    }

    private sealed class FakeMembershipService : IPostgresRoleMembershipService
    {
        public List<RoleMembership> Edges { get; } = new();
        public List<RoleMembershipChange> AppliedChanges { get; } = new();
        public string? LastMemberRole { get; private set; }
        public bool ThrowOnList { get; set; }
        public bool ThrowOnApply { get; set; }

        public Task<IReadOnlyList<RoleMembership>> ListMembershipsAsync(
            ConnectionProfile profile, string profilePassword, CancellationToken cancellationToken = default) =>
            ThrowOnList
                ? Task.FromException<IReadOnlyList<RoleMembership>>(new InvalidOperationException("no catalog access"))
                : Task.FromResult<IReadOnlyList<RoleMembership>>(Edges.ToList());

        public Task ApplyMembershipChangesAsync(
            ConnectionProfile profile, string profilePassword, string memberRole,
            IReadOnlyList<RoleMembershipChange> changes, CancellationToken cancellationToken = default)
        {
            if (ThrowOnApply)
                return Task.FromException(new InvalidOperationException("permission denied"));
            LastMemberRole = memberRole;
            AppliedChanges.AddRange(changes);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMembershipEditor : IRoleMembershipEditor
    {
        public IReadOnlyList<RoleMembershipChange>? NextResult { get; set; }
        public IReadOnlyList<RoleSummary>? LastAllRoles { get; private set; }
        public IReadOnlyCollection<string>? LastCurrentGroups { get; private set; }

        public Task<IReadOnlyList<RoleMembershipChange>?> EditAsync(
            RoleSummary member, IReadOnlyList<RoleSummary> allRoles,
            IReadOnlyCollection<string> currentGroups, CancellationToken cancellationToken = default)
        {
            LastAllRoles = allRoles;
            LastCurrentGroups = currentGroups;
            return Task.FromResult(NextResult);
        }
    }

    private sealed class EmptyResources : IStringResources
    {
        public string? Get(string key, CultureInfo culture) => null;
    }

    private sealed class FakeProfileService : IConnectionProfileService
    {
        public Dictionary<Guid, string> SavedPasswords { get; } = new();

        public Task<IReadOnlyList<ConnectionProfile>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConnectionProfile>>(Array.Empty<ConnectionProfile>());

        public Task<ConnectionProfile?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<ConnectionProfile?>(null);

        public Task<string?> GetPasswordAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(SavedPasswords.TryGetValue(id, out var pwd) ? pwd : null);

        public Task SaveAsync(ConnectionProfile profile, string password, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class FakeMetadataService : IPostgresMetadataService
    {
        private Func<ConnectionProfile, IReadOnlyList<DatabaseSummary>> _databaseResolver;
        private Func<ConnectionProfile, IReadOnlyList<RoleSummary>> _roleResolver;

        public ConnectionProfile? LastProfile { get; private set; }
        public string? LastPassword { get; private set; }

        public FakeMetadataService()
            : this(Array.Empty<string>())
        {
        }

        public FakeMetadataService(IReadOnlyList<string> databaseNames)
        {
            _databaseResolver = _ => databaseNames.Select(n => new DatabaseSummary(n)).ToList();
            _roleResolver = _ => Array.Empty<RoleSummary>();
        }

        public FakeMetadataService(Func<ConnectionProfile, IReadOnlyList<DatabaseSummary>> resolver)
        {
            _databaseResolver = resolver;
            _roleResolver = _ => Array.Empty<RoleSummary>();
        }

        public void SetResult(IReadOnlyList<string> databaseNames) =>
            _databaseResolver = _ => databaseNames.Select(n => new DatabaseSummary(n)).ToList();

        public void SetRoles(IReadOnlyList<RoleSummary> roles) =>
            _roleResolver = _ => roles;

        public Task<IReadOnlyList<DatabaseSummary>> ListDatabasesAsync(
            ConnectionProfile profile,
            string password,
            CancellationToken cancellationToken = default)
        {
            LastProfile = profile;
            LastPassword = password;
            return Task.FromResult(_databaseResolver(profile));
        }

        public Task<IReadOnlyList<RoleSummary>> ListRolesAsync(
            ConnectionProfile profile,
            string password,
            CancellationToken cancellationToken = default)
        {
            LastProfile = profile;
            LastPassword = password;
            return Task.FromResult(_roleResolver(profile));
        }

        public Task<IReadOnlyList<SchemaSummary>> ListSchemasAsync(
            ConnectionProfile profile,
            string password,
            string databaseName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SchemaSummary>>(Array.Empty<SchemaSummary>());
    }

    private sealed class FakeRoleAdminService : IPostgresRoleAdminService
    {
        public List<CreateRoleRequest> CreatedRoles { get; } = new();
        public List<AlterRoleRequest> AlteredRoles { get; } = new();
        public List<string> DroppedRoles { get; } = new();
        public string? LastPassword { get; private set; }
        public string? LastAlterPassword { get; private set; }
        public string? LastDropPassword { get; private set; }
        public Exception? ThrowOnCreate { get; set; }
        public Exception? ThrowOnAlter { get; set; }
        public Exception? ThrowOnDrop { get; set; }

        public Task CreateRoleAsync(
            ConnectionProfile profile,
            string profilePassword,
            CreateRoleRequest request,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnCreate is not null)
                throw ThrowOnCreate;
            LastPassword = profilePassword;
            CreatedRoles.Add(request);
            return Task.CompletedTask;
        }

        public Task AlterRoleAsync(
            ConnectionProfile profile,
            string profilePassword,
            AlterRoleRequest request,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnAlter is not null)
                throw ThrowOnAlter;
            LastAlterPassword = profilePassword;
            AlteredRoles.Add(request);
            return Task.CompletedTask;
        }

        public Task DropRoleAsync(
            ConnectionProfile profile,
            string profilePassword,
            string roleName,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnDrop is not null)
                throw ThrowOnDrop;
            LastDropPassword = profilePassword;
            DroppedRoles.Add(roleName);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRoleEditor : IRoleEditor
    {
        public CreateRoleRequest? NextRequest { get; set; }
        public AlterRoleRequest? NextAlterRequest { get; set; }
        public RoleSummary? LastEditTarget { get; private set; }
        public bool? LastCanLoginDefault { get; private set; }

        public Task<CreateRoleRequest?> CreateAsync(bool canLoginDefault = false, CancellationToken cancellationToken = default)
        {
            LastCanLoginDefault = canLoginDefault;
            return Task.FromResult(NextRequest);
        }

        public Task<AlterRoleRequest?> EditAsync(RoleSummary current, CancellationToken cancellationToken = default)
        {
            LastEditTarget = current;
            return Task.FromResult(NextAlterRequest);
        }
    }

    private sealed class FakeConfirmationDialog : IConfirmationDialog
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
