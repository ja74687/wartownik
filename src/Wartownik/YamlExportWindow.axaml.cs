using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Wartownik.Yaml;

namespace Wartownik;

public partial class YamlExportWindow : Window
{
    public YamlExportWindow()
    {
        InitializeComponent();
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
    private void OnCloseButtonClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not YamlExportRequest request)
            return;
        if (Clipboard is { } clipboard)
            await clipboard.SetTextAsync(request.Yaml).ConfigureAwait(true);
    }

    /// <summary>
    /// Save dialog uses the modern StorageProvider API (Avalonia 12) so it picks the right
    /// native picker on each OS and respects sandbox permissions on Linux/macOS.
    /// </summary>
    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not YamlExportRequest request)
            return;
        if (StorageProvider is not { } provider)
            return;

        var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save YAML export",
            SuggestedFileName = request.DefaultFileName,
            DefaultExtension = "yaml",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("YAML")
                {
                    Patterns = new[] { "*.yaml", "*.yml" },
                },
            },
        }).ConfigureAwait(true);

        if (file is null)
            return;

        await using var stream = await file.OpenWriteAsync().ConfigureAwait(true);
        var bytes = Encoding.UTF8.GetBytes(request.Yaml);
        await stream.WriteAsync(bytes).ConfigureAwait(true);
    }
}
