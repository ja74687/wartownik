using System.ComponentModel;
using System.Globalization;

namespace Wartownik.Localization;

public sealed class LocalizationService : ILocalizationService
{
    private readonly IStringResources _resources;
    private CultureInfo _currentLanguage;

    public LocalizationService(
        IStringResources resources,
        IReadOnlyList<CultureInfo> availableLanguages,
        CultureInfo initialLanguage)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(availableLanguages);
        ArgumentNullException.ThrowIfNull(initialLanguage);
        if (availableLanguages.Count == 0)
            throw new ArgumentException("At least one language must be available.", nameof(availableLanguages));
        if (!ContainsCulture(availableLanguages, initialLanguage))
            throw new ArgumentException(
                $"Initial language '{initialLanguage.Name}' is not in the available list.",
                nameof(initialLanguage));

        _resources = resources;
        AvailableLanguages = availableLanguages;
        _currentLanguage = initialLanguage;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public CultureInfo CurrentLanguage => _currentLanguage;

    public IReadOnlyList<CultureInfo> AvailableLanguages { get; }

    public string this[string key] => Get(key);

    public string Get(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return _resources.Get(key, _currentLanguage) ?? key;
    }

    public void SetLanguage(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        if (!ContainsCulture(AvailableLanguages, culture))
            throw new ArgumentException(
                $"Language '{culture.Name}' is not available.",
                nameof(culture));

        if (string.Equals(_currentLanguage.Name, culture.Name, StringComparison.OrdinalIgnoreCase))
            return;

        _currentLanguage = culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    private static bool ContainsCulture(IReadOnlyList<CultureInfo> cultures, CultureInfo target)
    {
        foreach (var c in cultures)
            if (string.Equals(c.Name, target.Name, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
