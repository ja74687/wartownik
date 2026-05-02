using Wartownik.ViewModels;

namespace Wartownik.UnitTests.ViewModels;

public class AsyncRelayCommandTests
{
    [Fact]
    public async Task ExecuteAsync_invokes_handler()
    {
        var invoked = 0;
        var sut = new AsyncRelayCommand(() => { invoked++; return Task.CompletedTask; });

        await sut.ExecuteAsync();

        Assert.Equal(1, invoked);
    }

    [Fact]
    public async Task CanExecute_returns_false_while_running_then_true_after()
    {
        var tcs = new TaskCompletionSource();
        var sut = new AsyncRelayCommand(() => tcs.Task);

        var execution = sut.ExecuteAsync();

        Assert.False(sut.CanExecute(null));

        tcs.SetResult();
        await execution;

        Assert.True(sut.CanExecute(null));
    }

    [Fact]
    public async Task ExecuteAsync_does_nothing_when_already_running()
    {
        var calls = 0;
        var tcs = new TaskCompletionSource();
        var sut = new AsyncRelayCommand(() => { calls++; return tcs.Task; });

        var first = sut.ExecuteAsync();
        await sut.ExecuteAsync(); // should be ignored while first is in flight

        tcs.SetResult();
        await first;

        Assert.Equal(1, calls);
    }

    [Fact]
    public void CanExecute_respects_predicate()
    {
        var allow = false;
        var sut = new AsyncRelayCommand(() => Task.CompletedTask, () => allow);

        Assert.False(sut.CanExecute(null));
        allow = true;
        Assert.True(sut.CanExecute(null));
    }

    [Fact]
    public async Task RaiseCanExecuteChanged_fires_when_running_state_toggles()
    {
        var fires = 0;
        var tcs = new TaskCompletionSource();
        var sut = new AsyncRelayCommand(() => tcs.Task);
        sut.CanExecuteChanged += (_, _) => fires++;

        var execution = sut.ExecuteAsync();
        Assert.Equal(1, fires);

        tcs.SetResult();
        await execution;

        Assert.Equal(2, fires);
    }

    [Fact]
    public async Task ExecuteAsync_propagates_exception()
    {
        var sut = new AsyncRelayCommand(() => throw new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ExecuteAsync());
        Assert.True(sut.CanExecute(null));
    }
}
