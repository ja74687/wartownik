namespace Wartownik.Connections;

public interface IPostgresRoleAdminService
{
    Task CreateRoleAsync(
        ConnectionProfile profile,
        string profilePassword,
        CreateRoleRequest request,
        CancellationToken cancellationToken = default);

    Task AlterRoleAsync(
        ConnectionProfile profile,
        string profilePassword,
        AlterRoleRequest request,
        CancellationToken cancellationToken = default);

    Task DropRoleAsync(
        ConnectionProfile profile,
        string profilePassword,
        string roleName,
        CancellationToken cancellationToken = default);
}

public sealed record CreateRoleRequest(
    string RoleName,
    bool IsSuperuser,
    bool CanCreateDb,
    bool CanCreateRole,
    bool CanLogin,
    string? RolePassword);

public sealed record AlterRoleRequest(
    string RoleName,
    bool IsSuperuser,
    bool CanCreateDb,
    bool CanCreateRole,
    bool CanLogin,
    string? NewPassword);
