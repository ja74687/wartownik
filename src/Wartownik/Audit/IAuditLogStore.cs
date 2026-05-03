namespace Wartownik.Audit;

/// <summary>
/// Local persistence for "what did Wartownik change, when, and what was the outcome".
/// Append-only: entries never get edited or deleted in normal operation.
/// </summary>
public interface IAuditLogStore
{
    Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the most recent entries first. Optional filters narrow by profile and database.
    /// </summary>
    Task<IReadOnlyList<AuditEntry>> ListAsync(
        Guid? profileId = null,
        string? databaseName = null,
        int max = 200,
        CancellationToken cancellationToken = default);
}

public enum AuditOutcome
{
    Success,
    Failed,
}

/// <summary>
/// One Apply record. Statements are stored verbatim so the SQL log can replay exactly what ran.
/// </summary>
public sealed record AuditEntry(
    Guid Id,
    DateTimeOffset Timestamp,
    Guid ProfileId,
    string ProfileName,
    string DatabaseName,
    string TargetRoleName,
    IReadOnlyList<string> Statements,
    AuditOutcome Outcome,
    string? ErrorMessage,
    string Executor);
