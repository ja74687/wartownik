using Microsoft.Extensions.DependencyInjection;
using Wartownik.Composition;
using Wartownik.Connections;
using Wartownik.Connections.Credentials;
using Wartownik.Dialogs;
using Wartownik.Localization;
using Wartownik.Postgres;
using Wartownik.Sql;
using Wartownik.ViewModels;

namespace Wartownik.UnitTests.Composition;

public class BootstrapperTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ServiceProvider _provider;

    public BootstrapperTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"wartownik-bootstrap-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddWartownik(new AppPaths(_tempDir));
        _provider = services.BuildServiceProvider();
    }

    [Fact]
    public void Resolves_AppPaths_singleton()
    {
        var a = _provider.GetRequiredService<AppPaths>();
        var b = _provider.GetRequiredService<AppPaths>();

        Assert.Same(a, b);
        Assert.Equal(_tempDir, a.DataDirectory);
    }

    [Fact]
    public void Resolves_sql_validator()
    {
        var validator = _provider.GetRequiredService<ISqlStatementValidator>();

        Assert.IsType<PostgresSqlStatementValidator>(validator);
    }

    [Fact]
    public void Resolves_postgres_session_factory_wrapped_with_validating_decorator()
    {
        var factory = _provider.GetRequiredService<IPostgresSessionFactory>();

        Assert.IsType<ValidatingPostgresSessionFactory>(factory);
    }

    [Fact]
    public void Resolves_npgsql_connection_string_factory()
    {
        var factory = _provider.GetRequiredService<NpgsqlConnectionStringFactory>();

        Assert.NotNull(factory);
    }

    [Fact]
    public void Resolves_connection_profile_store()
    {
        var store = _provider.GetRequiredService<IConnectionProfileStore>();

        Assert.IsType<JsonConnectionProfileStore>(store);
    }

    [Fact]
    public void Resolves_credential_store_for_current_platform()
    {
        var store = _provider.GetRequiredService<ICredentialStore>();

        Assert.NotNull(store);
    }

    [Fact]
    public void Resolves_localization_service_with_supported_languages()
    {
        var localization = _provider.GetRequiredService<ILocalizationService>();

        Assert.Equal(Bootstrapper.DefaultLanguage.Name, localization.CurrentLanguage.Name);
        Assert.Equal(Bootstrapper.SupportedLanguages.Count, localization.AvailableLanguages.Count);
    }

    [Fact]
    public void Localization_service_is_singleton()
    {
        var a = _provider.GetRequiredService<ILocalizationService>();
        var b = _provider.GetRequiredService<ILocalizationService>();

        Assert.Same(a, b);
    }

    [Fact]
    public void Resolves_main_window_view_model_as_transient()
    {
        var a = _provider.GetRequiredService<MainWindowViewModel>();
        var b = _provider.GetRequiredService<MainWindowViewModel>();

        Assert.NotNull(a);
        Assert.NotSame(a, b);
    }

    [Fact]
    public void Resolves_connection_profile_service()
    {
        var service = _provider.GetRequiredService<IConnectionProfileService>();

        Assert.IsType<ConnectionProfileService>(service);
    }

    [Fact]
    public void Resolves_connection_profile_editor()
    {
        var editor = _provider.GetRequiredService<IConnectionProfileEditor>();

        Assert.NotNull(editor);
    }

    [Fact]
    public void Resolves_connection_tester()
    {
        var tester = _provider.GetRequiredService<IConnectionTester>();

        Assert.IsType<PostgresConnectionTester>(tester);
    }

    [Fact]
    public void Resolves_confirmation_dialog()
    {
        var dialog = _provider.GetRequiredService<IConfirmationDialog>();

        Assert.NotNull(dialog);
    }

    [Fact]
    public void Resolves_postgres_metadata_service()
    {
        var metadata = _provider.GetRequiredService<IPostgresMetadataService>();

        Assert.IsType<PostgresMetadataService>(metadata);
    }

    [Fact]
    public void Resolves_postgres_role_admin_service()
    {
        var admin = _provider.GetRequiredService<IPostgresRoleAdminService>();

        Assert.IsType<PostgresRoleAdminService>(admin);
    }

    [Fact]
    public void Resolves_role_editor()
    {
        var editor = _provider.GetRequiredService<IRoleEditor>();

        Assert.NotNull(editor);
    }

    [Fact]
    public void Resolves_role_editor_view_model_as_transient()
    {
        var a = _provider.GetRequiredService<RoleEditorViewModel>();
        var b = _provider.GetRequiredService<RoleEditorViewModel>();

        Assert.NotSame(a, b);
    }

    [Fact]
    public void Resolves_profile_details_factory()
    {
        var factory = _provider.GetRequiredService<MainWindowViewModel.ProfileDetailsFactory>();

        Assert.NotNull(factory);
    }

    [Fact]
    public void Resolves_database_details_factory()
    {
        var factory = _provider.GetRequiredService<ProfileDetailsViewModel.DatabaseDetailsFactory>();

        Assert.NotNull(factory);
    }

    [Fact]
    public void Resolves_connection_profile_editor_view_model_as_transient()
    {
        var a = _provider.GetRequiredService<ConnectionProfileEditorViewModel>();
        var b = _provider.GetRequiredService<ConnectionProfileEditorViewModel>();

        Assert.NotSame(a, b);
    }

    [Fact]
    public void AddWartownik_throws_on_null_arguments()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ((IServiceCollection)null!).AddWartownik(new AppPaths(_tempDir)));
        Assert.Throws<ArgumentNullException>(() =>
            new ServiceCollection().AddWartownik(null!));
    }

    public void Dispose()
    {
        _provider.Dispose();
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; tests already finished.
        }
    }
}
