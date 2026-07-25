using System.Globalization;
using Wartownik.Connections;
using Wartownik.Localization;
using Wartownik.ViewModels;

namespace Wartownik.UnitTests.ViewModels;

public class RoleMembershipEditorViewModelTests
{
    private static readonly CultureInfo English = new("en");

    private static RoleMembershipEditorViewModel Build() =>
        new(new LocalizationService(new EmptyResources(), new[] { English }, English));

    private static RoleSummary Role(string name, bool canLogin = false) =>
        new(name, IsSuperuser: false, CanCreateDb: false, CanCreateRole: false, CanLogin: canLogin);

    [Fact]
    public void LoadFor_lists_every_other_role_as_a_candidate()
    {
        var vm = Build();

        vm.LoadFor(Role("alice", canLogin: true), new[] { Role("alice", true), Role("devs"), Role("analysts") }, []);

        Assert.Equal(new[] { "devs", "analysts" }, vm.Options.Select(o => o.GroupName));
        Assert.True(vm.HasOptions);
    }

    [Fact]
    public void LoadFor_never_offers_the_role_itself()
    {
        var vm = Build();

        vm.LoadFor(Role("devs"), new[] { Role("devs") }, []);

        Assert.Empty(vm.Options); // a role can't be a member of itself
        Assert.False(vm.HasOptions);
    }

    [Fact]
    public void LoadFor_ticks_the_groups_the_role_already_belongs_to()
    {
        var vm = Build();

        vm.LoadFor(Role("alice", true), new[] { Role("devs"), Role("analysts") }, new[] { "devs" });

        Assert.True(vm.Options.Single(o => o.GroupName == "devs").IsMember);
        Assert.False(vm.Options.Single(o => o.GroupName == "analysts").IsMember);
    }

    [Fact]
    public void BuildChanges_is_empty_when_nothing_was_touched()
    {
        var vm = Build();
        vm.LoadFor(Role("alice", true), new[] { Role("devs"), Role("analysts") }, new[] { "devs" });

        Assert.Empty(vm.BuildChanges());
    }

    [Fact]
    public void Ticking_a_group_produces_a_grant()
    {
        var vm = Build();
        vm.LoadFor(Role("alice", true), new[] { Role("devs") }, []);

        vm.Options.Single().IsMember = true;

        var change = Assert.Single(vm.BuildChanges());
        Assert.Equal("devs", change.GroupRole);
        Assert.Equal(GrantOperation.Grant, change.Operation);
    }

    [Fact]
    public void Unticking_a_group_produces_a_revoke()
    {
        var vm = Build();
        vm.LoadFor(Role("alice", true), new[] { Role("devs") }, new[] { "devs" });

        vm.Options.Single().IsMember = false;

        var change = Assert.Single(vm.BuildChanges());
        Assert.Equal("devs", change.GroupRole);
        Assert.Equal(GrantOperation.Revoke, change.Operation);
    }

    [Fact]
    public void Toggling_a_group_back_to_its_original_state_produces_nothing()
    {
        var vm = Build();
        vm.LoadFor(Role("alice", true), new[] { Role("devs") }, []);
        var option = vm.Options.Single();

        option.IsMember = true;
        option.IsMember = false;

        Assert.Empty(vm.BuildChanges()); // only real deltas are applied
    }

    [Fact]
    public void BuildChanges_only_includes_the_groups_that_changed()
    {
        var vm = Build();
        vm.LoadFor(
            Role("alice", true),
            new[] { Role("devs"), Role("analysts"), Role("ops") },
            new[] { "devs" });

        vm.Options.Single(o => o.GroupName == "analysts").IsMember = true; // join
        // devs stays ticked, ops stays unticked — neither may appear

        var change = Assert.Single(vm.BuildChanges());
        Assert.Equal("analysts", change.GroupRole);
    }

    [Fact]
    public void LoadFor_replaces_the_previous_role_state()
    {
        var vm = Build();
        vm.LoadFor(Role("alice", true), new[] { Role("devs") }, new[] { "devs" });

        vm.LoadFor(Role("bob", true), new[] { Role("devs"), Role("ops") }, []);

        Assert.Equal(2, vm.Options.Count);
        Assert.All(vm.Options, o => Assert.False(o.IsMember)); // no leftovers from alice
        Assert.Empty(vm.BuildChanges());
    }

    private sealed class EmptyResources : IStringResources
    {
        public string? Get(string key, CultureInfo culture) => null;
    }
}
