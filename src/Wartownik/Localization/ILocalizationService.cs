using System.ComponentModel;
using System.Globalization;

namespace Wartownik.Localization;

public interface ILocalizationService : INotifyPropertyChanged
{
    CultureInfo CurrentLanguage { get; }

    IReadOnlyList<CultureInfo> AvailableLanguages { get; }

    string this[string key] { get; }

    string Get(string key);

    void SetLanguage(CultureInfo culture);
}
