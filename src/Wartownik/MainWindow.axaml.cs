using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Wartownik.ViewModels;

namespace Wartownik;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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
