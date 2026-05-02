using System.Globalization;
using Wartownik.Connections;
using Wartownik.Localization;
using Wartownik.ViewModels;

namespace Wartownik.UnitTests.ViewModels;

public class RoleEditorViewModelTests
{
    private static readonly CultureInfo English = new("en");

    private static RoleEditorViewModel Create(IStringResources? resources = null)
    {
        var loc = new LocalizationService(
            resources ?? new EmptyResources(),
            new[] { English, new CultureInfo("pl") },
            English);
        return new RoleEditorViewModel(loc);
    }

    [Fact]
    public void Default_state_is_no_login_no_flags_and_not_edit_mode()
    {
        var sut = Create();

        Assert.Equal("", sut.RoleName);
        Assert.False(sut.IsSuperuser);
        Assert.False(sut.CanCreateDb);
        Assert.False(sut.CanCreateRole);
        Assert.False(sut.CanLogin);
        Assert.False(sut.IsPasswordEnabled);
        Assert.False(sut.IsEditMode);
        Assert.True(sut.IsRoleNameEditable);
    }

    [Fact]
    public void IsPasswordEnabled_follows_CanLogin()
    {
        var sut = Create();
        Assert.False(sut.IsPasswordEnabled);

        sut.CanLogin = true;
        Assert.True(sut.IsPasswordEnabled);

        sut.CanLogin = false;
        Assert.False(sut.IsPasswordEnabled);
    }

    [Fact]
    public void TryBuildCreate_returns_request_with_trimmed_name()
    {
        var sut = Create();
        sut.RoleName = "  alice  ";
        sut.IsSuperuser = true;
        sut.CanLogin = true;
        sut.RolePassword = "pw";

        Assert.True(sut.TryBuildCreate(out var request));
        Assert.Equal("alice", request.RoleName);
        Assert.True(request.IsSuperuser);
        Assert.True(request.CanLogin);
        Assert.Equal("pw", request.RolePassword);
    }

    [Fact]
    public void TryBuildCreate_drops_password_when_not_login()
    {
        var sut = Create();
        sut.RoleName = "group_a";
        sut.CanLogin = false;
        sut.RolePassword = "should-be-ignored";

        Assert.True(sut.TryBuildCreate(out var request));
        Assert.Null(request.RolePassword);
    }

    [Fact]
    public void TryBuildCreate_fails_when_role_name_blank()
    {
        var sut = Create();
        sut.RoleName = "   ";

        Assert.False(sut.TryBuildCreate(out _));
        Assert.False(string.IsNullOrEmpty(sut.ErrorMessage));
    }

    [Fact]
    public void LoadFrom_marks_edit_mode_and_disables_role_name_editing()
    {
        var sut = Create();
        var role = new RoleSummary("alice",
            IsSuperuser: true, CanCreateDb: true, CanCreateRole: true, CanLogin: true);

        sut.LoadFrom(role);

        Assert.True(sut.IsEditMode);
        Assert.False(sut.IsRoleNameEditable);
        Assert.Equal("alice", sut.RoleName);
        Assert.True(sut.IsSuperuser);
        Assert.True(sut.CanCreateDb);
        Assert.True(sut.CanCreateRole);
        Assert.True(sut.CanLogin);
        Assert.Equal("", sut.RolePassword);
    }

    [Fact]
    public void TryBuildAlter_uses_loaded_name_and_returns_alter_request()
    {
        var sut = Create();
        sut.LoadFrom(new RoleSummary("alice", IsSuperuser: false, CanCreateDb: false, CanCreateRole: false, CanLogin: true));
        sut.IsSuperuser = true;
        sut.CanCreateDb = true;
        sut.RolePassword = "newpw";

        Assert.True(sut.TryBuildAlter(out var request));
        Assert.Equal("alice", request.RoleName);
        Assert.True(request.IsSuperuser);
        Assert.True(request.CanCreateDb);
        Assert.True(request.CanLogin);
        Assert.Equal("newpw", request.NewPassword);
    }

    [Fact]
    public void TryBuildAlter_omits_new_password_when_blank()
    {
        var sut = Create();
        sut.LoadFrom(new RoleSummary("alice", IsSuperuser: false, CanCreateDb: false, CanCreateRole: false, CanLogin: true));
        // No password change -> NewPassword should be null

        Assert.True(sut.TryBuildAlter(out var request));
        Assert.Null(request.NewPassword);
    }

    [Fact]
    public void TryBuildAlter_omits_new_password_when_no_login()
    {
        var sut = Create();
        sut.LoadFrom(new RoleSummary("group_a", IsSuperuser: false, CanCreateDb: false, CanCreateRole: false, CanLogin: false));
        sut.RolePassword = "ignored";

        Assert.True(sut.TryBuildAlter(out var request));
        Assert.Null(request.NewPassword);
    }

    [Fact]
    public void Title_uses_New_in_add_mode_and_Edit_after_LoadFrom()
    {
        var resources = new MapResources()
            .With("Role.New", "Add new")
            .With("Role.Edit", "Edit existing");
        var sut = Create(resources);

        Assert.Equal("Add new", sut.Title);

        sut.LoadFrom(new RoleSummary("alice", false, false, false, false));
        Assert.Equal("Edit existing", sut.Title);
    }

    [Fact]
    public void Setting_field_raises_PropertyChanged()
    {
        var sut = Create();
        var changes = new List<string?>();
        sut.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        sut.RoleName = "x";
        sut.CanLogin = true;

        Assert.Contains(nameof(RoleEditorViewModel.RoleName), changes);
        Assert.Contains(nameof(RoleEditorViewModel.CanLogin), changes);
        Assert.Contains(nameof(RoleEditorViewModel.IsPasswordEnabled), changes);
    }

    [Fact]
    public void Constructor_throws_on_null_localization()
    {
        Assert.Throws<ArgumentNullException>(() => new RoleEditorViewModel(null!));
    }

    [Fact]
    public void LoadFrom_throws_on_null()
    {
        var sut = Create();
        Assert.Throws<ArgumentNullException>(() => sut.LoadFrom(null!));
    }

    private sealed class EmptyResources : IStringResources
    {
        public string? Get(string key, CultureInfo culture) => null;
    }

    private sealed class MapResources : IStringResources
    {
        private readonly Dictionary<string, string> _entries = new();

        public MapResources With(string key, string value)
        {
            _entries[key] = value;
            return this;
        }

        public string? Get(string key, CultureInfo culture) =>
            _entries.TryGetValue(key, out var value) ? value : null;
    }
}
