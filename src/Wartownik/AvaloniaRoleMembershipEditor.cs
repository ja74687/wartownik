using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;
using Wartownik.Connections;
using Wartownik.ViewModels;

namespace Wartownik;

public sealed class AvaloniaRoleMembershipEditor : IRoleMembershipEditor
{
    private readonly IServiceProvider _services;

    public AvaloniaRoleMembershipEditor(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    public async Task<IReadOnlyList<RoleMembershipChange>?> EditAsync(
        RoleSummary member,
        IReadOnlyList<RoleSummary> allRoles,
        IReadOnlyCollection<string> currentGroups,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(allRoles);
        ArgumentNullException.ThrowIfNull(currentGroups);

        var viewModel = _services.GetRequiredService<RoleMembershipEditorViewModel>();
        viewModel.LoadFor(member, allRoles, currentGroups);

        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow
            ?? throw new InvalidOperationException("MainWindow is not available.");

        var window = new RoleMembershipEditorWindow { DataContext = viewModel };
        var result = await window.ShowDialog<object?>(owner).ConfigureAwait(true);
        return result as IReadOnlyList<RoleMembershipChange>;
    }
}
