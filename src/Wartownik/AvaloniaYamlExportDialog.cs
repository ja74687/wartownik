using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Wartownik.Yaml;

namespace Wartownik;

public sealed class AvaloniaYamlExportDialog : IYamlExportDialog
{
    public async Task ShowAsync(YamlExportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow
            ?? throw new InvalidOperationException("MainWindow is not available.");

        var window = new YamlExportWindow { DataContext = request };
        await window.ShowDialog(owner).ConfigureAwait(true);
    }
}
