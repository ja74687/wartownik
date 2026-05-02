using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Wartownik.Connections;
using Wartownik.ViewModels;

namespace Wartownik;

public partial class ConnectionProfileEditorWindow : Window
{
    public ConnectionProfileEditorWindow()
    {
        InitializeComponent();
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ConnectionProfileEditorViewModel vm)
            return;

        if (!vm.TryBuild(out var profile, out var password))
            return;

        Close(new ConnectionProfileEditResult(profile, password));
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
