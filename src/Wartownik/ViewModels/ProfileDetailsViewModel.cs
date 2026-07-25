using System.Collections.ObjectModel;
using Wartownik.Connections;
using Wartownik.Dialogs;
using Wartownik.Localization;

namespace Wartownik.ViewModels;

public sealed class ProfileDetailsViewModel : ViewModelBase
{
    public delegate DatabaseDetailsViewModel DatabaseDetailsFactory(ConnectionProfile profile, DatabaseSummary summary);

    private readonly IConnectionProfileService _profiles;
    private readonly IPostgresMetadataService _metadata;
    private readonly IPostgresRoleAdminService _roleAdmin;
    private readonly IRoleEditor _roleEditor;
    private readonly IConfirmationDialog _confirmation;
    private readonly DatabaseDetailsFactory _databaseFactory;
    private readonly IPostgresRoleMembershipService? _membership;
    private readonly IRoleMembershipEditor? _membershipEditor;

    /// <summary>All roles in the cluster from the last load — the candidate groups for membership.</summary>
    private IReadOnlyList<RoleSummary> _allRoles = Array.Empty<RoleSummary>();

    private DatabaseDetailsViewModel? _selectedDatabase;

    private bool _isLoading;
    private string? _errorMessage;

    public ConnectionProfile Profile { get; }
    public ILocalizationService Localization { get; }
    public ObservableCollection<DatabaseItemViewModel> Databases { get; } = new();
    public ObservableCollection<RoleItemViewModel> Users { get; } = new();
    public ObservableCollection<RoleItemViewModel> Roles { get; } = new();

    public AsyncRelayCommand AddUserCommand { get; }
    public AsyncRelayCommand AddRoleCommand { get; }
    public AsyncRelayCommand EditRoleCommand { get; }
    public AsyncRelayCommand DropRoleCommand { get; }
    public AsyncRelayCommand OpenDatabaseCommand { get; }
    public AsyncRelayCommand BackToDatabasesCommand { get; }
    public AsyncRelayCommand EditMembershipCommand { get; }

    public ProfileDetailsViewModel(
        ConnectionProfile profile,
        ILocalizationService localization,
        IConnectionProfileService profiles,
        IPostgresMetadataService metadata,
        IPostgresRoleAdminService roleAdmin,
        IRoleEditor roleEditor,
        IConfirmationDialog confirmation,
        DatabaseDetailsFactory databaseFactory,
        IPostgresRoleMembershipService? membership = null,
        IRoleMembershipEditor? membershipEditor = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(roleAdmin);
        ArgumentNullException.ThrowIfNull(roleEditor);
        ArgumentNullException.ThrowIfNull(confirmation);
        ArgumentNullException.ThrowIfNull(databaseFactory);

        Profile = profile;
        Localization = localization;
        _profiles = profiles;
        _metadata = metadata;
        _roleAdmin = roleAdmin;
        _roleEditor = roleEditor;
        _confirmation = confirmation;
        _databaseFactory = databaseFactory;
        _membership = membership;
        _membershipEditor = membershipEditor;

        AddUserCommand = new AsyncRelayCommand(() => AddRoleAsync(canLoginDefault: true));
        AddRoleCommand = new AsyncRelayCommand(() => AddRoleAsync(canLoginDefault: false));
        EditRoleCommand = new AsyncRelayCommand(parameter => EditRoleAsync(parameter));
        DropRoleCommand = new AsyncRelayCommand(parameter => DropRoleAsync(parameter));
        OpenDatabaseCommand = new AsyncRelayCommand(parameter => OpenDatabaseAsync(parameter));
        BackToDatabasesCommand = new AsyncRelayCommand(BackToDatabasesAsync);
        EditMembershipCommand = new AsyncRelayCommand(parameter => EditMembershipAsync(parameter));
    }

    /// <summary>
    /// Membership editing needs both the catalog read and the dialog; when either is missing
    /// (unit tests build the VM without them) the UI hides the entry point instead of failing.
    /// </summary>
    public bool CanEditMembership => _membership is not null && _membershipEditor is not null;

    public DatabaseDetailsViewModel? SelectedDatabase
    {
        get => _selectedDatabase;
        private set
        {
            if (SetField(ref _selectedDatabase, value))
                RaisePropertyChanged(nameof(IsViewingDatabase));
        }
    }

    public bool IsViewingDatabase => _selectedDatabase is not null;

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetField(ref _isLoading, value))
            {
                RaisePropertyChanged(nameof(IsContentVisible));
                RaisePropertyChanged(nameof(IsDatabasesEmpty));
                RaisePropertyChanged(nameof(IsUsersEmpty));
                RaisePropertyChanged(nameof(IsRolesEmpty));
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value))
            {
                RaisePropertyChanged(nameof(HasError));
                RaisePropertyChanged(nameof(IsContentVisible));
                RaisePropertyChanged(nameof(IsDatabasesEmpty));
                RaisePropertyChanged(nameof(IsUsersEmpty));
                RaisePropertyChanged(nameof(IsRolesEmpty));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_errorMessage);

    public bool HasDatabases => Databases.Count > 0;
    public bool HasUsers => Users.Count > 0;
    public bool HasRoles => Roles.Count > 0;

    public bool IsDatabasesEmpty => !IsLoading && !HasError && !HasDatabases;
    public bool IsUsersEmpty => !IsLoading && !HasError && !HasUsers;
    public bool IsRolesEmpty => !IsLoading && !HasError && !HasRoles;

    public bool IsContentVisible => !IsLoading && !HasError;

    public string Endpoint => $"{Profile.Host}:{Profile.Port} / {Profile.Database} / {Profile.Username}";

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        ErrorMessage = null;
        Databases.Clear();
        Users.Clear();
        Roles.Clear();
        RaiseListSnapshotProperties();

        try
        {
            var password = await _profiles
                .GetPasswordAsync(Profile.Id, cancellationToken)
                .ConfigureAwait(true) ?? "";

            var databases = await _metadata
                .ListDatabasesAsync(Profile, password, cancellationToken)
                .ConfigureAwait(true);

            foreach (var summary in databases)
            {
                var item = new DatabaseItemViewModel(summary, Localization)
                {
                    LastSyncAt = DateTimeOffset.UtcNow,
                };
                Databases.Add(item);
                _ = RefreshDatabaseMetaAsync(item, password);
            }

            var roles = await _metadata
                .ListRolesAsync(Profile, password, cancellationToken)
                .ConfigureAwait(true);
            _allRoles = roles;

            var memberships = await LoadMembershipsAsync(password, cancellationToken).ConfigureAwait(true);

            foreach (var summary in roles)
            {
                var item = new RoleItemViewModel(summary, Localization);
                if (memberships.TryGetValue(summary.Name, out var groups))
                    item.MemberOf = groups;
                if (summary.CanLogin)
                    Users.Add(item);
                else
                    Roles.Add(item);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
            RaiseListSnapshotProperties();
        }
    }

    /// <summary>
    /// Membership edges grouped by member name. Best-effort: on an older server or without
    /// catalog access the roles list still loads, just without the "member of" line.
    /// </summary>
    private async Task<Dictionary<string, IReadOnlyList<string>>> LoadMembershipsAsync(
        string password,
        CancellationToken cancellationToken)
    {
        if (_membership is null)
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        try
        {
            var edges = await _membership
                .ListMembershipsAsync(Profile, password, cancellationToken)
                .ConfigureAwait(true);

            return edges
                .GroupBy(e => e.MemberRole, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<string>)g.Select(e => e.GroupRole).ToList(),
                    StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        }
    }

    private async Task EditMembershipAsync(object? parameter)
    {
        if (parameter is not RoleItemViewModel item)
            return;
        if (_membership is null || _membershipEditor is null)
            return;

        var changes = await _membershipEditor
            .EditAsync(item.Summary, _allRoles, item.MemberOf)
            .ConfigureAwait(true);

        if (changes is null || changes.Count == 0)
            return;

        try
        {
            var password = await _profiles
                .GetPasswordAsync(Profile.Id)
                .ConfigureAwait(true) ?? "";
            await _membership
                .ApplyMembershipChangesAsync(Profile, password, item.Name, changes)
                .ConfigureAwait(true);

            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task RefreshDatabaseMetaAsync(DatabaseItemViewModel item, string password)
    {
        // Best-effort per-database metadata fetch in background. We don't surface errors here —
        // pills just stay hidden if the user lacks privileges or the connection fails.
        try
        {
            var schemas = await _metadata
                .ListSchemasAsync(Profile, password, item.Name)
                .ConfigureAwait(true);
            item.SchemaCount = schemas.Count;
            item.LastSyncAt = DateTimeOffset.UtcNow;
        }
        catch
        {
            // swallow — leave pill hidden
        }
    }

    private async Task AddRoleAsync(bool canLoginDefault)
    {
        var request = await _roleEditor.CreateAsync(canLoginDefault).ConfigureAwait(true);
        if (request is null)
            return;

        try
        {
            var password = await _profiles
                .GetPasswordAsync(Profile.Id)
                .ConfigureAwait(true) ?? "";
            await _roleAdmin
                .CreateRoleAsync(Profile, password, request)
                .ConfigureAwait(true);

            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task OpenDatabaseAsync(object? parameter)
    {
        if (parameter is not DatabaseItemViewModel item)
            return;

        var dbVm = _databaseFactory(Profile, item.Summary);
        SelectedDatabase = dbVm;
        await dbVm.LoadAsync().ConfigureAwait(true);
    }

    private Task BackToDatabasesAsync()
    {
        SelectedDatabase = null;
        return Task.CompletedTask;
    }

    private async Task EditRoleAsync(object? parameter)
    {
        if (parameter is not RoleItemViewModel item)
            return;

        var request = await _roleEditor.EditAsync(item.Summary).ConfigureAwait(true);
        if (request is null)
            return;

        try
        {
            var password = await _profiles
                .GetPasswordAsync(Profile.Id)
                .ConfigureAwait(true) ?? "";
            await _roleAdmin
                .AlterRoleAsync(Profile, password, request)
                .ConfigureAwait(true);

            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task DropRoleAsync(object? parameter)
    {
        if (parameter is not RoleItemViewModel item)
            return;

        var request = new ConfirmationRequest(
            Title: Localization["Roles.DropConfirmTitle"],
            Message: string.Format(Localization["Roles.DropConfirmMessage"], item.Name),
            ConfirmLabel: Localization["Roles.Drop"],
            CancelLabel: Localization["Common.Cancel"],
            IsDestructive: true);

        if (!await _confirmation.ConfirmAsync(request).ConfigureAwait(true))
            return;

        try
        {
            var password = await _profiles
                .GetPasswordAsync(Profile.Id)
                .ConfigureAwait(true) ?? "";
            await _roleAdmin
                .DropRoleAsync(Profile, password, item.Name)
                .ConfigureAwait(true);

            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private void RaiseListSnapshotProperties()
    {
        RaisePropertyChanged(nameof(HasDatabases));
        RaisePropertyChanged(nameof(HasUsers));
        RaisePropertyChanged(nameof(HasRoles));
        RaisePropertyChanged(nameof(IsDatabasesEmpty));
        RaisePropertyChanged(nameof(IsUsersEmpty));
        RaisePropertyChanged(nameof(IsRolesEmpty));
    }
}
