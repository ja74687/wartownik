using Wartownik.Connections;

namespace Wartownik.ViewModels;

public sealed class DatabaseItemViewModel
{
    public DatabaseSummary Summary { get; }

    public DatabaseItemViewModel(DatabaseSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        Summary = summary;
    }

    public string Name => Summary.Name;
}
