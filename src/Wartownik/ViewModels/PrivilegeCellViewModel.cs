using Wartownik.Connections;

namespace Wartownik.ViewModels;

public enum CellState
{
    NotGranted,
    Granted,
    PendingGrant,    // currently NOT granted, user toggled to grant
    PendingRevoke,   // currently granted, user toggled to revoke
}

/// <summary>
/// One checkbox in the matrix. Tracks the value the database currently reports
/// (CurrentValue) versus the value the user has staged in the UI (PendingValue).
/// State is derived from those two — the matrix VM observes State changes to
/// recompute the global pending list.
/// </summary>
public sealed class PrivilegeCellViewModel : ViewModelBase
{
    private readonly Action _onChanged;

    private bool _currentValue;
    private bool _pendingValue;

    public PrivilegeCellViewModel(GrantPrivilege privilege, bool initialValue, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(onChanged);
        Privilege = privilege;
        _currentValue = initialValue;
        _pendingValue = initialValue;
        _onChanged = onChanged;
        ToggleCommand = new RelayCommand(_ => Toggle());
    }

    public GrantPrivilege Privilege { get; }
    public RelayCommand ToggleCommand { get; }

    public bool CurrentValue
    {
        get => _currentValue;
        private set
        {
            if (SetField(ref _currentValue, value))
                RaiseDerivedChanged();
        }
    }

    public bool PendingValue
    {
        get => _pendingValue;
        private set
        {
            if (SetField(ref _pendingValue, value))
            {
                RaiseDerivedChanged();
                _onChanged();
            }
        }
    }

    public bool IsDirty => _currentValue != _pendingValue;

    public CellState State => (_currentValue, _pendingValue) switch
    {
        (false, false) => CellState.NotGranted,
        (true, true)   => CellState.Granted,
        (false, true)  => CellState.PendingGrant,
        (true, false)  => CellState.PendingRevoke,
    };

    // Plain BMP Unicode glyphs — render reliably without depending on Segoe Fluent Icons being
    // installed. The matrix VM exposes per-state booleans below so XAML can pick a colour class.
    public string Glyph => State switch
    {
        CellState.Granted        => "✓",  // ✓ CHECK MARK
        CellState.NotGranted     => "–",  // – EN DASH
        CellState.PendingGrant   => "+",       // plain plus reads fine in any font
        CellState.PendingRevoke  => "✕",  // ✕ MULTIPLICATION X
        _                        => "?",
    };

    public bool IsGranted       => State == CellState.Granted;
    public bool IsNotGranted    => State == CellState.NotGranted;
    public bool IsPendingGrant  => State == CellState.PendingGrant;
    public bool IsPendingRevoke => State == CellState.PendingRevoke;

    public void Toggle() => PendingValue = !_pendingValue;

    /// <summary>
    /// Reset pending value back to current — used by Discard.
    /// </summary>
    public void DiscardPending()
    {
        if (_pendingValue == _currentValue)
            return;
        _pendingValue = _currentValue;
        RaisePropertyChanged(nameof(PendingValue));
        RaiseDerivedChanged();
        _onChanged();
    }

    /// <summary>
    /// Adopt a freshly-loaded value from the database as the new "current" baseline.
    /// Pending follows it (no dirty state after a refresh).
    /// </summary>
    public void RebaseFromCurrent(bool newCurrent)
    {
        var changed = _currentValue != newCurrent || _pendingValue != newCurrent;
        _currentValue = newCurrent;
        _pendingValue = newCurrent;
        if (changed)
        {
            RaisePropertyChanged(nameof(CurrentValue));
            RaisePropertyChanged(nameof(PendingValue));
            RaiseDerivedChanged();
        }
    }

    private void RaiseDerivedChanged()
    {
        RaisePropertyChanged(nameof(IsDirty));
        RaisePropertyChanged(nameof(State));
        RaisePropertyChanged(nameof(Glyph));
        RaisePropertyChanged(nameof(IsGranted));
        RaisePropertyChanged(nameof(IsNotGranted));
        RaisePropertyChanged(nameof(IsPendingGrant));
        RaisePropertyChanged(nameof(IsPendingRevoke));
    }
}
