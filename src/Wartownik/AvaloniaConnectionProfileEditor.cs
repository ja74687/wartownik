using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;
using Wartownik.Connections;
using Wartownik.ViewModels;

namespace Wartownik;

public sealed class AvaloniaConnectionProfileEditor : IConnectionProfileEditor
{
    private readonly IServiceProvider _services;

    public AvaloniaConnectionProfileEditor(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    public Task<ConnectionProfileEditResult?> AddAsync(CancellationToken cancellationToken = default)
    {
        var viewModel = _services.GetRequiredService<ConnectionProfileEditorViewModel>();
        return ShowDialogAsync(viewModel);
    }

    public Task<ConnectionProfileEditResult?> EditAsync(
        ConnectionProfile profile,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(password);

        var viewModel = _services.GetRequiredService<ConnectionProfileEditorViewModel>();
        viewModel.LoadFrom(profile, password);
        return ShowDialogAsync(viewModel);
    }

    private static async Task<ConnectionProfileEditResult?> ShowDialogAsync(ConnectionProfileEditorViewModel viewModel)
    {
        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow
            ?? throw new InvalidOperationException("MainWindow is not available.");

        var window = new ConnectionProfileEditorWindow { DataContext = viewModel };
        return await window.ShowDialog<ConnectionProfileEditResult?>(owner).ConfigureAwait(true);
    }
}
