using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Wartownik.ViewModels;

namespace Wartownik;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        // Only offer to copy when files are being dragged in.
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    /// <summary>
    /// Import connection profiles from dropped .json files (the tip on the profile list
    /// promises this). Each file's contents go to the VM, which parses and saves them.
    /// </summary>
    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var files = e.DataTransfer.TryGetFiles();
        if (files is null)
            return;

        foreach (var file in files.OfType<IStorageFile>())
        {
            if (!file.Name.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                await using var stream = await file.OpenReadAsync();
                using var reader = new System.IO.StreamReader(stream);
                var json = await reader.ReadToEndAsync();
                await vm.ImportProfilesFromJsonAsync(json);
            }
            catch
            {
                // Skip files we can't read; the VM surfaces parse errors for ones we can.
            }
        }
    }

    /// <summary>
    /// Click handler for permissions matrix cells. We use Border + Tapped instead of Button so
    /// we can fully control the visual without Semi.Avalonia's button template overriding the
    /// glyph font. The Border's DataContext is the cell VM that knows how to flip itself.
    /// </summary>
    private void OnMatrixCellTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: PrivilegeCellViewModel cell })
            cell.Toggle();
    }

    /// <summary>
    /// Click handler for the schema row's "ALL" toggle cell. DataContext is the row VM.
    /// </summary>
    private void OnMatrixToggleAllTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: SchemaPermissionRowViewModel row })
            row.ToggleAll();
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e) => ToggleMaximize();

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object? sender, RoutedEventArgs e) => ToggleMaximize();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
}
