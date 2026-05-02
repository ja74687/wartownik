using Wartownik.Composition;

namespace Wartownik.UnitTests.Composition;

public class AppPathsTests
{
    [Fact]
    public void Paths_are_built_relative_to_data_directory()
    {
        var paths = new AppPaths(@"C:\test\data");

        Assert.Equal(@"C:\test\data", paths.DataDirectory);
        Assert.Equal(Path.Combine(@"C:\test\data", "profiles.json"), paths.ProfilesFilePath);
        Assert.Equal(Path.Combine(@"C:\test\data", "credentials.json"), paths.CredentialsFilePath);
    }

    [Fact]
    public void Constructor_throws_on_blank_data_directory()
    {
        Assert.Throws<ArgumentException>(() => new AppPaths(""));
        Assert.Throws<ArgumentException>(() => new AppPaths("   "));
        Assert.Throws<ArgumentNullException>(() => new AppPaths(null!));
    }

    [Fact]
    public void Default_uses_application_data_with_app_subdirectory()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        var paths = AppPaths.Default();

        Assert.Equal(Path.Combine(roaming, AppPaths.AppDirectoryName), paths.DataDirectory);
    }
}
