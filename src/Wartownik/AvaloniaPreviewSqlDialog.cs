using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Wartownik.Dialogs;

namespace Wartownik;

public sealed class AvaloniaPreviewSqlDialog : IPreviewSqlDialog
{
    public async Task ShowAsync(PreviewSqlRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow
            ?? throw new InvalidOperationException("MainWindow is not available.");

        var window = new PreviewSqlWindow { DataContext = request };
        await window.ShowDialog(owner).ConfigureAwait(true);
    }
}
