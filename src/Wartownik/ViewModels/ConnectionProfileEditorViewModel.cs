using System.ComponentModel;
using Wartownik.Connections;
using Wartownik.Localization;

namespace Wartownik.ViewModels;

public enum ConnectionTestState
{
    Idle,
    Testing,
    Success,
    Failed,
}

public sealed class ConnectionProfileEditorViewModel : ViewModelBase
{
    private static readonly HashSet<string> InputProperties = new()
    {
        nameof(DisplayName), nameof(Host), nameof(Port),
        nameof(Database), nameof(Username), nameof(Password), nameof(SslMode),
    };

    private readonly IConnectionTester _tester;

    private Guid? _existingId;
    private string _displayName = "";
    private string _host = "";
    private int _port = ConnectionProfile.DefaultPort;
    private string _database = "";
    private string _username = "";
    private string _password = "";
    private PostgresSslMode _sslMode = PostgresSslMode.Require;
    private string? _errorMessage;
    private bool _isEditMode;
    private ConnectionTestState _testState = ConnectionTestState.Idle;
    private string? _testErrorMessage;

    public ILocalizationService Localization { get; }
    public AsyncRelayCommand TestCommand { get; }

    public ConnectionProfileEditorViewModel(
        ILocalizationService localization,
        IConnectionTester tester)
    {
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(tester);
        Localization = localization;
        _tester = tester;
        Localization.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "Item[]")
                RaisePropertyChanged(nameof(Title));
        };

        TestCommand = new AsyncRelayCommand(TestAsync);
        PropertyChanged += OnSelfPropertyChanged;
    }

    public bool IsEditMode
    {
        get => _isEditMode;
        private set
        {
            if (SetField(ref _isEditMode, value))
                RaisePropertyChanged(nameof(Title));
        }
    }

    public string Title => Localization[IsEditMode ? "Profile.Edit" : "Profile.New"];

    public string DisplayName
    {
        get => _displayName;
        set => SetField(ref _displayName, value);
    }

    public string Host
    {
        get => _host;
        set => SetField(ref _host, value);
    }

    public int Port
    {
        get => _port;
        set => SetField(ref _port, value);
    }

    public string Database
    {
        get => _database;
        set => SetField(ref _database, value);
    }

    public string Username
    {
        get => _username;
        set => SetField(ref _username, value);
    }

    public string Password
    {
        get => _password;
        set => SetField(ref _password, value);
    }

    public PostgresSslMode SslMode
    {
        get => _sslMode;
        set => SetField(ref _sslMode, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    public ConnectionTestState TestState
    {
        get => _testState;
        private set
        {
            if (SetField(ref _testState, value))
            {
                RaisePropertyChanged(nameof(IsTesting));
                RaisePropertyChanged(nameof(TestSucceeded));
                RaisePropertyChanged(nameof(TestFailed));
                RaisePropertyChanged(nameof(IsTestStateVisible));
            }
        }
    }

    public string? TestErrorMessage
    {
        get => _testErrorMessage;
        private set => SetField(ref _testErrorMessage, value);
    }

    public bool IsTesting => _testState == ConnectionTestState.Testing;
    public bool TestSucceeded => _testState == ConnectionTestState.Success;
    public bool TestFailed => _testState == ConnectionTestState.Failed;
    public bool IsTestStateVisible => _testState != ConnectionTestState.Idle;

    public IReadOnlyList<PostgresSslMode> AvailableSslModes { get; } =
        Enum.GetValues<PostgresSslMode>();

    public void LoadFrom(ConnectionProfile profile, string password)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(password);

        _existingId = profile.Id;
        DisplayName = profile.DisplayName;
        Host = profile.Host;
        Port = profile.Port;
        Database = profile.Database;
        Username = profile.Username;
        Password = password;
        SslMode = profile.SslMode;
        ErrorMessage = null;
        TestState = ConnectionTestState.Idle;
        TestErrorMessage = null;
        IsEditMode = true;
    }

    public bool TryBuild(out ConnectionProfile profile, out string password)
    {
        password = Password;
        try
        {
            profile = _existingId.HasValue
                ? ConnectionProfile.Create(_existingId.Value, DisplayName, Host, Port, Database, Username, SslMode)
                : ConnectionProfile.Create(DisplayName, Host, Port, Database, Username, SslMode);
            ErrorMessage = null;
            return true;
        }
        catch (ArgumentException ex)
        {
            profile = null!;
            ErrorMessage = ex.Message;
            return false;
        }
    }

    private async Task TestAsync()
    {
        if (!TryBuild(out var profile, out var password))
            return;

        TestState = ConnectionTestState.Testing;
        TestErrorMessage = null;

        var result = await _tester.TestAsync(profile, password).ConfigureAwait(true);

        if (result.Success)
        {
            TestState = ConnectionTestState.Success;
            TestErrorMessage = null;
        }
        else
        {
            TestState = ConnectionTestState.Failed;
            TestErrorMessage = result.ErrorMessage;
        }
    }

    private void OnSelfPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null) return;
        if (!InputProperties.Contains(e.PropertyName)) return;
        if (_testState == ConnectionTestState.Testing) return;
        TestState = ConnectionTestState.Idle;
    }
}
