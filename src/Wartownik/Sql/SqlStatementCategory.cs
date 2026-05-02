namespace Wartownik.Sql;

public enum SqlStatementCategory
{
    ReadMetadata,
    RoleManagement,
    GrantRevoke,
    DefaultPrivileges,
}

public static class SqlStatementCategoryExtensions
{
    public static bool IsReadOnly(this SqlStatementCategory category) =>
        category == SqlStatementCategory.ReadMetadata;
}
