using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Wartownik.Connections;
using Wartownik.Connections.Credentials;
using Wartownik.Dialogs;
using Wartownik.Localization;
using Wartownik.Postgres;
using Wartownik.Sql;
using Wartownik.ViewModels;

namespace Wartownik.Composition;

public static class Bootstrapper
{
    public static readonly CultureInfo English = new("en");
    public static readonly CultureInfo Polish = new("pl");

    public static IReadOnlyList<CultureInfo> SupportedLanguages { get; } = [English, Polish];
    public static CultureInfo DefaultLanguage => English;

    public static IServiceCollection AddWartownik(this IServiceCollection services, AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(paths);

        services.AddSingleton(paths);

        services.AddSingleton<ISqlStatementValidator, PostgresSqlStatementValidator>();

        services.AddSingleton<NpgsqlConnectionStringFactory>();
        services.AddSingleton<NpgsqlPostgresSessionFactory>();
        services.AddSingleton<IPostgresSessionFactory>(sp =>
            new ValidatingPostgresSessionFactory(
                sp.GetRequiredService<NpgsqlPostgresSessionFactory>(),
                sp.GetRequiredService<ISqlStatementValidator>()));

        services.AddSingleton<IConnectionProfileStore>(sp =>
            new JsonConnectionProfileStore(sp.GetRequiredService<AppPaths>().ProfilesFilePath));

        services.AddSingleton<ICredentialStore>(sp => CreateCredentialStore(sp.GetRequiredService<AppPaths>()));

        services.AddSingleton<IConnectionProfileService, ConnectionProfileService>();
        services.AddSingleton<IConnectionProfileEditor, AvaloniaConnectionProfileEditor>();
        services.AddSingleton<IConnectionTester, PostgresConnectionTester>();
        services.AddSingleton<IPostgresMetadataService, PostgresMetadataService>();
        services.AddSingleton<IPostgresRoleAdminService, PostgresRoleAdminService>();
        services.AddSingleton<IRoleEditor, AvaloniaRoleEditor>();
        services.AddSingleton<IConfirmationDialog, AvaloniaConfirmationDialog>();

        services.AddSingleton<IStringResources>(_ => ResourceManagerStringResources.ForApplicationStrings());
        services.AddSingleton<ILocalizationService>(sp =>
            new LocalizationService(
                sp.GetRequiredService<IStringResources>(),
                SupportedLanguages,
                DefaultLanguage));

        services.AddTransient<ConnectionProfileEditorViewModel>();
        services.AddTransient<RoleEditorViewModel>();
        services.AddSingleton<ProfileDetailsViewModel.DatabaseDetailsFactory>(sp =>
            (profile, dbName) => new DatabaseDetailsViewModel(
                profile,
                dbName,
                sp.GetRequiredService<ILocalizationService>(),
                sp.GetRequiredService<IConnectionProfileService>(),
                sp.GetRequiredService<IPostgresMetadataService>()));

        services.AddSingleton<MainWindowViewModel.ProfileDetailsFactory>(sp =>
            profile => new ProfileDetailsViewModel(
                profile,
                sp.GetRequiredService<ILocalizationService>(),
                sp.GetRequiredService<IConnectionProfileService>(),
                sp.GetRequiredService<IPostgresMetadataService>(),
                sp.GetRequiredService<IPostgresRoleAdminService>(),
                sp.GetRequiredService<IRoleEditor>(),
                sp.GetRequiredService<IConfirmationDialog>(),
                sp.GetRequiredService<ProfileDetailsViewModel.DatabaseDetailsFactory>()));

        // MainWindowViewModel needs IConnectionTester + IPostgresMetadataService for
        // background per-profile status + counter refresh on the list view.
        services.AddTransient<MainWindowViewModel>();

        return services;
    }

    private static ICredentialStore CreateCredentialStore(AppPaths paths)
    {
        if (OperatingSystem.IsWindows())
        {
#pragma warning disable CA1416
            return new WindowsDpapiCredentialStore(AppPaths.CredentialServiceName, paths.CredentialsFilePath);
#pragma warning restore CA1416
        }

        return CredentialStoreFactory.CreateForCurrentPlatform(AppPaths.CredentialServiceName);
    }
}
