namespace Wartownik.Sql;

public sealed record SqlValidationResult(
    bool IsAllowed,
    SqlStatementCategory? Category,
    string? RejectionReason)
{
    public static SqlValidationResult Allow(SqlStatementCategory category) =>
        new(true, category, null);

    public static SqlValidationResult Reject(string reason) =>
        new(false, null, reason);
}
