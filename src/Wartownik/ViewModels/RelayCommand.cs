using System.Windows.Input;

namespace Wartownik.ViewModels;

/// <summary>
/// Synchronous ICommand. Use AsyncRelayCommand for any I/O — this one is for cheap
/// in-memory operations like toggling a checkbox.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
            return;
        _execute(parameter);
    }

    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
