namespace Wartownik.Connections;

public interface IRoleEditor
{
    Task<CreateRoleRequest?> CreateAsync(bool canLoginDefault = false, CancellationToken cancellationToken = default);

    Task<AlterRoleRequest?> EditAsync(RoleSummary current, CancellationToken cancellationToken = default);
}
