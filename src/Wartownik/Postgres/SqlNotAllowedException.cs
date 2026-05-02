namespace Wartownik.Postgres;

public sealed class SqlNotAllowedException : InvalidOperationException
{
    public string Sql { get; }
    public string Reason { get; }

    public SqlNotAllowedException(string sql, string reason)
        : base($"SQL statement was rejected by the validator: {reason}")
    {
        Sql = sql;
        Reason = reason;
    }
}
