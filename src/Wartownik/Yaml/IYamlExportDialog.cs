namespace Wartownik.Yaml;

/// <summary>
/// Shows the generated YAML in a preview dialog with copy-to-clipboard / save-to-file actions.
/// </summary>
public interface IYamlExportDialog
{
    Task ShowAsync(YamlExportRequest request, CancellationToken cancellationToken = default);
}

public sealed record YamlExportRequest(string DefaultFileName, string Yaml);
