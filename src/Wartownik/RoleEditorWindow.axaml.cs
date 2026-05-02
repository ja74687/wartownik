using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Wartownik.ViewModels;

namespace Wartownik;

public partial class RoleEditorWindow : Window
{
    public RoleEditorWindow()
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
        if (DataContext is not RoleEditorViewModel vm)
            return;

        if (vm.IsEditMode)
        {
            if (vm.TryBuildAlter(out var alter))
                Close(alter);
        }
        else
        {
            if (vm.TryBuildCreate(out var create))
                Close(create);
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close(null);
}
