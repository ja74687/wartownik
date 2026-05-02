using System.Globalization;
using Wartownik.Localization;

namespace Wartownik.UnitTests.Localization;

public class ResourceManagerStringResourcesTests
{
    private static readonly CultureInfo English = new("en");
    private static readonly CultureInfo Polish = new("pl");

    private readonly IStringResources _resources = ResourceManagerStringResources.ForApplicationStrings();

    [Fact]
    public void Brand_string_is_identical_in_both_languages()
    {
        Assert.Equal("Wartownik", _resources.Get("App.Title", English));
        Assert.Equal("Wartownik", _resources.Get("App.Title", Polish));
    }

    [Fact]
    public void Localized_string_differs_per_language()
    {
        Assert.Equal("English", _resources.Get("Languages.English", English));
        Assert.Equal("Angielski", _resources.Get("Languages.English", Polish));

        Assert.Equal("Polish", _resources.Get("Languages.Polish", English));
        Assert.Equal("Polski", _resources.Get("Languages.Polish", Polish));
    }

    [Fact]
    public void Get_returns_null_for_unknown_key()
    {
        Assert.Null(_resources.Get("Does.Not.Exist", English));
    }

    [Fact]
    public void Get_falls_back_to_neutral_for_unknown_culture()
    {
        // German has no resx — ResourceManager falls back through the parent chain
        // to the neutral (default) resources, which is English in our setup.
        Assert.Equal("English", _resources.Get("Languages.English", new CultureInfo("de")));
    }

    [Fact]
    public void Get_throws_on_blank_key_or_null_culture()
    {
        Assert.Throws<ArgumentException>(() => _resources.Get("", English));
        Assert.Throws<ArgumentNullException>(() => _resources.Get(null!, English));
        Assert.Throws<ArgumentNullException>(() => _resources.Get("App.Title", null!));
    }
}
