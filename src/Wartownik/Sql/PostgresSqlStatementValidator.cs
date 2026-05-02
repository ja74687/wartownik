using System.Text;
using System.Text.RegularExpressions;

namespace Wartownik.Sql;

public sealed class PostgresSqlStatementValidator : ISqlStatementValidator
{
    private static readonly Regex DefaultPrivilegesPattern =
        new(@"^ALTER\s+DEFAULT\s+PRIVILEGES\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RoleManagementPattern =
        new(@"^(CREATE|ALTER|DROP)\s+(ROLE|USER|GROUP)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex GrantRevokePattern =
        new(@"^(GRANT|REVOKE)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SelectPattern =
        new(@"^SELECT\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FromOrJoinSourcePattern =
        new(@"\b(FROM|JOIN)\s+([A-Za-z_][\w""\.]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public SqlValidationResult Validate(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        var stripped = StripCommentsAndStringLiterals(sql);

        var multiStatementError = CheckSingleStatement(stripped);
        if (multiStatementError is not null)
            return SqlValidationResult.Reject(multiStatementError);

        var normalized = stripped.TrimEnd(';', ' ', '\t', '\n', '\r').TrimStart();
        if (normalized.Length == 0)
            return SqlValidationResult.Reject("Empty statement");

        if (DefaultPrivilegesPattern.IsMatch(normalized))
            return SqlValidationResult.Allow(SqlStatementCategory.DefaultPrivileges);

        if (RoleManagementPattern.IsMatch(normalized))
            return SqlValidationResult.Allow(SqlStatementCategory.RoleManagement);

        if (GrantRevokePattern.IsMatch(normalized))
            return SqlValidationResult.Allow(SqlStatementCategory.GrantRevoke);

        if (SelectPattern.IsMatch(normalized))
            return ValidateSelect(normalized);

        var leadingToken = normalized.Split([' ', '\t', '\n', '\r'], 2)[0];
        return SqlValidationResult.Reject($"Statement type not allowed: '{leadingToken}'");
    }

    private static SqlValidationResult ValidateSelect(string normalized)
    {
        var sources = FromOrJoinSourcePattern.Matches(normalized);
        if (sources.Count == 0)
            return SqlValidationResult.Reject("SELECT without FROM is not a metadata read");

        foreach (Match match in sources)
        {
            var source = match.Groups[2].Value.Trim('"');
            if (!IsSystemCatalogSource(source))
                return SqlValidationResult.Reject(
                    $"SELECT source '{source}' is not a system catalog (pg_*, pg_catalog.*, information_schema.*)");
        }

        return SqlValidationResult.Allow(SqlStatementCategory.ReadMetadata);
    }

    private static bool IsSystemCatalogSource(string source) =>
        source.StartsWith("pg_", StringComparison.OrdinalIgnoreCase) ||
        source.StartsWith("pg_catalog.", StringComparison.OrdinalIgnoreCase) ||
        source.StartsWith("information_schema.", StringComparison.OrdinalIgnoreCase);

    // Strips line/block comments and the *contents* of string literals (keeps the
    // surrounding quotes). Replacing with whitespace prevents accidental token merging
    // and prevents semicolons hidden in strings/comments from triggering multi-statement
    // detection. Dollar-quoted strings ($$...$$) are not yet handled — out of MVP scope.
    private static string StripCommentsAndStringLiterals(string sql)
    {
        var sb = new StringBuilder(sql.Length);
        int i = 0;
        while (i < sql.Length)
        {
            char c = sql[i];

            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                i += 2;
                while (i < sql.Length && sql[i] != '\n') i++;
                continue;
            }

            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                i += 2;
                while (i < sql.Length - 1 && !(sql[i] == '*' && sql[i + 1] == '/')) i++;
                if (i < sql.Length - 1) i += 2;
                else i = sql.Length;
                sb.Append(' ');
                continue;
            }

            if (c == '\'')
            {
                sb.Append('\'');
                i++;
                while (i < sql.Length)
                {
                    if (sql[i] == '\'' && i + 1 < sql.Length && sql[i + 1] == '\'')
                    {
                        i += 2;
                        continue;
                    }
                    if (sql[i] == '\'')
                    {
                        sb.Append('\'');
                        i++;
                        break;
                    }
                    i++;
                }
                continue;
            }

            if (c == '"')
            {
                sb.Append('"');
                i++;
                while (i < sql.Length && sql[i] != '"')
                {
                    sb.Append(sql[i]);
                    i++;
                }
                if (i < sql.Length)
                {
                    sb.Append('"');
                    i++;
                }
                continue;
            }

            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    private static string? CheckSingleStatement(string strippedSql)
    {
        int semiPos = -1;
        for (int i = 0; i < strippedSql.Length; i++)
        {
            char c = strippedSql[i];
            if (c == ';')
            {
                if (semiPos >= 0)
                    return "SQL contains multiple statements (more than one semicolon)";
                semiPos = i;
            }
            else if (semiPos >= 0 && !char.IsWhiteSpace(c))
            {
                return "SQL contains multiple statements (content after semicolon)";
            }
        }
        return null;
    }
}
