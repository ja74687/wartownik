using System.ComponentModel;
using System.Globalization;
using Wartownik.Localization;

namespace Wartownik.UnitTests.Localization;

public class LocalizationServiceTests
{
    private static readonly CultureInfo English = new("en");
    private static readonly CultureInfo Polish = new("pl");

    private static LocalizationService BuildService(
        DictionaryStringResources? resources = null,
        CultureInfo? initial = null)
    {
        resources ??= new DictionaryStringResources();
        return new LocalizationService(
            resources,
            new[] { English, Polish },
            initial ?? English);
    }

    [Fact]
    public void Get_returns_value_from_resources_for_current_language()
    {
        var resources = new DictionaryStringResources()
            .With("Greeting", English, "Hello")
            .With("Greeting", Polish, "Cześć");

        var sut = BuildService(resources, initial: English);

        Assert.Equal("Hello", sut.Get("Greeting"));
        Assert.Equal("Hello", sut["Greeting"]);
    }

    [Fact]
    public void Get_returns_key_when_resource_missing()
    {
        var sut = BuildService();

        Assert.Equal("Missing.Key", sut.Get("Missing.Key"));
    }

    [Fact]
    public void Get_throws_on_blank_key()
    {
        var sut = BuildService();

        Assert.Throws<ArgumentException>(() => sut.Get(""));
        Assert.Throws<ArgumentNullException>(() => sut.Get(null!));
    }

    [Fact]
    public void SetLanguage_changes_current_language_and_resolved_value()
    {
        var resources = new DictionaryStringResources()
            .With("Greeting", English, "Hello")
            .With("Greeting", Polish, "Cześć");

        var sut = BuildService(resources, initial: English);
        sut.SetLanguage(Polish);

        Assert.Equal(Polish.Name, sut.CurrentLanguage.Name);
        Assert.Equal("Cześć", sut.Get("Greeting"));
    }

    [Fact]
    public void SetLanguage_raises_PropertyChanged_for_indexer_and_current_language()
    {
        var sut = BuildService(initial: English);
        var changes = new List<string?>();
        sut.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        sut.SetLanguage(Polish);

        Assert.Contains("Item[]", changes);
        Assert.Contains(nameof(ILocalizationService.CurrentLanguage), changes);
    }

    [Fact]
    public void SetLanguage_does_not_raise_when_set_to_same_language()
    {
        var sut = BuildService(initial: English);
        var changes = new List<PropertyChangedEventArgs>();
        sut.PropertyChanged += (_, e) => changes.Add(e);

        sut.SetLanguage(English);

        Assert.Empty(changes);
    }

    [Fact]
    public void SetLanguage_throws_when_language_not_in_available_list()
    {
        var sut = BuildService(initial: English);

        Assert.Throws<ArgumentException>(() => sut.SetLanguage(new CultureInfo("de")));
    }

    [Fact]
    public void Constructor_throws_when_initial_language_not_in_available_list()
    {
        Assert.Throws<ArgumentException>(() =>
            new LocalizationService(
                new DictionaryStringResources(),
                new[] { English },
                Polish));
    }

    [Fact]
    public void Constructor_throws_on_empty_available_languages()
    {
        Assert.Throws<ArgumentException>(() =>
            new LocalizationService(
                new DictionaryStringResources(),
                Array.Empty<CultureInfo>(),
                English));
    }

    [Fact]
    public void Constructor_throws_on_null_arguments()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new LocalizationService(null!, new[] { English }, English));
        Assert.Throws<ArgumentNullException>(() =>
            new LocalizationService(new DictionaryStringResources(), null!, English));
        Assert.Throws<ArgumentNullException>(() =>
            new LocalizationService(new DictionaryStringResources(), new[] { English }, null!));
    }

    private sealed class DictionaryStringResources : IStringResources
    {
        private readonly Dictionary<(string Key, string CultureName), string> _entries = new();

        public DictionaryStringResources With(string key, CultureInfo culture, string value)
        {
            _entries[(key, culture.Name)] = value;
            return this;
        }

        public string? Get(string key, CultureInfo culture)
        {
            return _entries.TryGetValue((key, culture.Name), out var value) ? value : null;
        }
    }
}
