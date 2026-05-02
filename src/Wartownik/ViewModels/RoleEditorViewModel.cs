using System.ComponentModel;
using Wartownik.Connections;
using Wartownik.Localization;

namespace Wartownik.ViewModels;

public sealed class RoleEditorViewModel : ViewModelBase
{
    private string _roleName = "";
    private bool _isSuperuser;
    private bool _canCreateDb;
    private bool _canCreateRole;
    private bool _canLogin;
    private string _rolePassword = "";
    private string? _errorMessage;
    private bool _isEditMode;

    public ILocalizationService Localization { get; }

    public RoleEditorViewModel(ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);
        Localization = localization;
        Localization.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "Item[]")
                RaisePropertyChanged(nameof(Title));
        };
    }

    public bool IsEditMode
    {
        get => _isEditMode;
        private set
        {
            if (SetField(ref _isEditMode, value))
            {
                RaisePropertyChanged(nameof(Title));
                RaisePropertyChanged(nameof(IsRoleNameEditable));
            }
        }
    }

    public string Title => Localization[IsEditMode ? "Role.Edit" : "Role.New"];

    public bool IsRoleNameEditable => !IsEditMode;

    public string RoleName
    {
        get => _roleName;
        set => SetField(ref _roleName, value);
    }

    public bool IsSuperuser
    {
        get => _isSuperuser;
        set => SetField(ref _isSuperuser, value);
    }

    public bool CanCreateDb
    {
        get => _canCreateDb;
        set => SetField(ref _canCreateDb, value);
    }

    public bool CanCreateRole
    {
        get => _canCreateRole;
        set => SetField(ref _canCreateRole, value);
    }

    public bool CanLogin
    {
        get => _canLogin;
        set
        {
            if (SetField(ref _canLogin, value))
                RaisePropertyChanged(nameof(IsPasswordEnabled));
        }
    }

    public bool IsPasswordEnabled => CanLogin;

    public string RolePassword
    {
        get => _rolePassword;
        set => SetField(ref _rolePassword, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetField(ref _errorMessage, value);
    }

    public void LoadFrom(RoleSummary role)
    {
        ArgumentNullException.ThrowIfNull(role);
        RoleName = role.Name;
        IsSuperuser = role.IsSuperuser;
        CanCreateDb = role.CanCreateDb;
        CanCreateRole = role.CanCreateRole;
        CanLogin = role.CanLogin;
        RolePassword = "";
        ErrorMessage = null;
        IsEditMode = true;
    }

    public void ResetForCreate(bool canLoginDefault)
    {
        RoleName = "";
        IsSuperuser = false;
        CanCreateDb = false;
        CanCreateRole = false;
        CanLogin = canLoginDefault;
        RolePassword = "";
        ErrorMessage = null;
        IsEditMode = false;
    }

    public bool TryBuildCreate(out CreateRoleRequest request)
    {
        if (!ValidateName(out var name))
        {
            request = null!;
            return false;
        }

        request = new CreateRoleRequest(
            RoleName: name,
            IsSuperuser: IsSuperuser,
            CanCreateDb: CanCreateDb,
            CanCreateRole: CanCreateRole,
            CanLogin: CanLogin,
            RolePassword: CanLogin ? RolePassword : null);
        ErrorMessage = null;
        return true;
    }

    public bool TryBuildAlter(out AlterRoleRequest request)
    {
        if (!ValidateName(out var name))
        {
            request = null!;
            return false;
        }

        var newPassword = CanLogin && !string.IsNullOrEmpty(RolePassword) ? RolePassword : null;

        request = new AlterRoleRequest(
            RoleName: name,
            IsSuperuser: IsSuperuser,
            CanCreateDb: CanCreateDb,
            CanCreateRole: CanCreateRole,
            CanLogin: CanLogin,
            NewPassword: newPassword);
        ErrorMessage = null;
        return true;
    }

    private bool ValidateName(out string trimmed)
    {
        trimmed = (RoleName ?? "").Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            ErrorMessage = Localization["Role.Name"] + ": required";
            return false;
        }
        return true;
    }
}
