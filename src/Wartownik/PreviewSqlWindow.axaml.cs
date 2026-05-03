using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Wartownik.Dialogs;

namespace Wartownik;

public partial class PreviewSqlWindow : Window
{
    public PreviewSqlWindow()
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

    /// <summary>
    /// Flatten every group into one human-readable SQL script and put it on the clipboard so
    /// the user can paste it into psql / DataGrip for an out-of-band sanity check.
    /// </summary>
    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PreviewSqlRequest request)
            return;

        var sb = new StringBuilder();
        foreach (var group in request.Groups)
        {
            sb.AppendLine($"-- {group.RoleName}");
            sb.AppendLine("BEGIN;");
            foreach (var stmt in group.Statements)
                sb.AppendLine(stmt + ";");
            sb.AppendLine("COMMIT;");
            sb.AppendLine();
        }

        var clipboard = Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(sb.ToString().TrimEnd()).ConfigureAwait(true);
    }
}
