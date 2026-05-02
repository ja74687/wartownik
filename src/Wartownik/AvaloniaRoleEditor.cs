using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;
using Wartownik.Connections;
using Wartownik.ViewModels;

namespace Wartownik;

public sealed class AvaloniaRoleEditor : IRoleEditor
{
    private readonly IServiceProvider _services;

    public AvaloniaRoleEditor(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    public async Task<CreateRoleRequest?> CreateAsync(bool canLoginDefault = false, CancellationToken cancellationToken = default)
    {
        var viewModel = _services.GetRequiredService<RoleEditorViewModel>();
        viewModel.ResetForCreate(canLoginDefault);
        var result = await ShowDialogAsync(viewModel).ConfigureAwait(true);
        return result as CreateRoleRequest;
    }

    public async Task<AlterRoleRequest?> EditAsync(RoleSummary current, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        var viewModel = _services.GetRequiredService<RoleEditorViewModel>();
        viewModel.LoadFrom(current);
        var result = await ShowDialogAsync(viewModel).ConfigureAwait(true);
        return result as AlterRoleRequest;
    }

    private static async Task<object?> ShowDialogAsync(RoleEditorViewModel viewModel)
    {
        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow
            ?? throw new InvalidOperationException("MainWindow is not available.");

        var window = new RoleEditorWindow { DataContext = viewModel };
        return await window.ShowDialog<object?>(owner).ConfigureAwait(true);
    }
}
