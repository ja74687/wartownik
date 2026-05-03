namespace Wartownik.Dialogs;

/// <summary>
/// Read-only "this is what Apply will run" dialog. Lets the user review the generated
/// GRANT/REVOKE batch before pulling the trigger — and copy it out for review elsewhere.
/// </summary>
public interface IPreviewSqlDialog
{
    Task ShowAsync(PreviewSqlRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Per-role group of SQL statements that the matrix's Apply would execute.
/// One transaction per role (that's what PostgresGrantService.ApplyGrantsAsync does today).
/// </summary>
public sealed record PreviewSqlGroup(string RoleName, IReadOnlyList<string> Statements);

public sealed record PreviewSqlRequest(IReadOnlyList<PreviewSqlGroup> Groups, string Title);
