using System.Text;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Wartownik.Dialogs;

namespace Wartownik;

public sealed class AvaloniaProfileExportDialog : IProfileExportDialog
{
    public async Task ExportAsync(string suggestedFileName, string json, CancellationToken cancellationToken = default)
    {
        var main = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (main?.StorageProvider is not { } provider)
            return;

        var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export profile",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "json",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } },
            },
        }).ConfigureAwait(true);

        if (file is null)
            return;

        await using var stream = await file.OpenWriteAsync().ConfigureAwait(true);
        await stream.WriteAsync(Encoding.UTF8.GetBytes(json), cancellationToken).ConfigureAwait(true);
    }
}
