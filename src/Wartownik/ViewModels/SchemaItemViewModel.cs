using Wartownik.Connections;

namespace Wartownik.ViewModels;

public sealed class SchemaItemViewModel
{
    public SchemaSummary Summary { get; }

    public SchemaItemViewModel(SchemaSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        Summary = summary;
    }

    public string Name => Summary.Name;
}
