namespace Wartownik.Composition;

public sealed class AppPaths
{
    public const string AppDirectoryName = "Wartownik";
    public const string ProfilesFileName = "profiles.json";
    public const string CredentialsFileName = "credentials.json";
    public const string AuditLogFileName = "audit.jsonl";
    public const string CredentialServiceName = "Wartownik";

    public string DataDirectory { get; }

    public string ProfilesFilePath => Path.Combine(DataDirectory, ProfilesFileName);

    public string CredentialsFilePath => Path.Combine(DataDirectory, CredentialsFileName);

    public string AuditLogFilePath => Path.Combine(DataDirectory, AuditLogFileName);

    public AppPaths(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        DataDirectory = dataDirectory;
    }

    public static AppPaths Default()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return new AppPaths(Path.Combine(roaming, AppDirectoryName));
    }
}
