using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Wartownik.ViewModels;

namespace Wartownik;

public partial class RoleMembershipEditorWindow : Window
{
    public RoleMembershipEditorWindow()
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
        if (DataContext is not RoleMembershipEditorViewModel vm)
            return;

        Close(vm.BuildChanges());
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close(null);
}
