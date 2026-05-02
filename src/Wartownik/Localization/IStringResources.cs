using System.Globalization;

namespace Wartownik.Localization;

public interface IStringResources
{
    string? Get(string key, CultureInfo culture);
}
