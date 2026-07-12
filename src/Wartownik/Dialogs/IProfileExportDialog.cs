namespace Wartownik.Dialogs;

/// <summary>
/// Saves a single connection profile's shareable JSON to a file the user picks.
/// </summary>
public interface IProfileExportDialog
{
    Task ExportAsync(string suggestedFileName, string json, CancellationToken cancellationToken = default);
}
