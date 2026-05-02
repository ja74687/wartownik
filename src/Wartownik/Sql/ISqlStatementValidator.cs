namespace Wartownik.Sql;

public interface ISqlStatementValidator
{
    SqlValidationResult Validate(string sql);
}
