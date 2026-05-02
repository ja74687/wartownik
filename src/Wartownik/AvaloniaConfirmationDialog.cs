using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Wartownik.Dialogs;

namespace Wartownik;

public sealed class AvaloniaConfirmationDialog : IConfirmationDialog
{
    public async Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow
            ?? throw new InvalidOperationException("MainWindow is not available.");

        var window = new ConfirmationWindow { DataContext = request };
        return await window.ShowDialog<bool>(owner).ConfigureAwait(true);
    }
}
