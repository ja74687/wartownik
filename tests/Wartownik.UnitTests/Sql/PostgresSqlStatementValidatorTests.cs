using Wartownik.Sql;

namespace Wartownik.UnitTests.Sql;

public class PostgresSqlStatementValidatorTests
{
    private readonly ISqlStatementValidator _validator = new PostgresSqlStatementValidator();

    // ---------- Allowed: role management ----------

    [Theory]
    [InlineData("CREATE ROLE foo")]
    [InlineData("CREATE ROLE foo LOGIN PASSWORD 'secret'")]
    [InlineData("CREATE USER bar")]
    [InlineData("CREATE GROUP grp")]
    [InlineData("ALTER ROLE foo PASSWORD 'new'")]
    [InlineData("ALTER USER foo NOLOGIN")]
    [InlineData("DROP ROLE foo")]
    [InlineData("DROP USER bar")]
    [InlineData("DROP ROLE IF EXISTS foo")]
    public void Allows_role_management_statements(string sql)
    {
        var result = _validator.Validate(sql);
        Assert.True(result.IsAllowed, result.RejectionReason);
        Assert.Equal(SqlStatementCategory.RoleManagement, result.Category);
    }

    // ---------- Allowed: grant/revoke ----------

    [Theory]
    [InlineData("GRANT SELECT ON TABLE public.users TO reader")]
    [InlineData("GRANT ALL ON SCHEMA public TO admin")]
    [InlineData("GRANT USAGE ON SEQUENCE public.s1 TO reader")]
    [InlineData("GRANT EXECUTE ON FUNCTION public.f() TO reader")]
    [InlineData("GRANT CONNECT ON DATABASE mydb TO reader")]
    [InlineData("REVOKE ALL ON SCHEMA public FROM PUBLIC")]
    [InlineData("REVOKE SELECT ON TABLE public.users FROM reader")]
    [InlineData("GRANT analyst TO alice")]
    [InlineData("REVOKE analyst FROM alice")]
    public void Allows_grant_revoke_statements(string sql)
    {
        var result = _validator.Validate(sql);
        Assert.True(result.IsAllowed, result.RejectionReason);
        Assert.Equal(SqlStatementCategory.GrantRevoke, result.Category);
    }

    // ---------- Allowed: ALTER DEFAULT PRIVILEGES ----------

    [Theory]
    [InlineData("ALTER DEFAULT PRIVILEGES FOR ROLE owner GRANT SELECT ON TABLES TO reader")]
    [InlineData("ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON TABLES TO admin")]
    [InlineData("ALTER DEFAULT PRIVILEGES FOR ROLE owner REVOKE SELECT ON TABLES FROM reader")]
    public void Allows_alter_default_privileges(string sql)
    {
        var result = _validator.Validate(sql);
        Assert.True(result.IsAllowed, result.RejectionReason);
        Assert.Equal(SqlStatementCategory.DefaultPrivileges, result.Category);
    }

    // ---------- Allowed: read metadata ----------

    [Theory]
    [InlineData("SELECT * FROM pg_roles")]
    [InlineData("SELECT rolname FROM pg_catalog.pg_roles")]
    [InlineData("SELECT * FROM information_schema.tables")]
    [InlineData("SELECT r.rolname FROM pg_roles r JOIN pg_auth_members m ON r.oid = m.member")]
    [InlineData("SELECT * FROM pg_namespace WHERE nspname = 'public'")]
    public void Allows_read_from_system_catalogs(string sql)
    {
        var result = _validator.Validate(sql);
        Assert.True(result.IsAllowed, result.RejectionReason);
        Assert.Equal(SqlStatementCategory.ReadMetadata, result.Category);
    }

    // ---------- Rejected: DML ----------

    [Theory]
    [InlineData("INSERT INTO public.users VALUES (1)")]
    [InlineData("UPDATE public.users SET name = 'x'")]
    [InlineData("DELETE FROM public.users")]
    [InlineData("TRUNCATE public.users")]
    [InlineData("MERGE INTO public.users USING src ON id = id WHEN MATCHED THEN UPDATE SET x = 1")]
    [InlineData("COPY public.users FROM '/tmp/x.csv'")]
    public void Rejects_dml(string sql)
    {
        var result = _validator.Validate(sql);
        Assert.False(result.IsAllowed);
        Assert.Null(result.Category);
    }

    // ---------- Rejected: schema/object DDL ----------

    [Theory]
    [InlineData("CREATE TABLE foo (id int)")]
    [InlineData("CREATE SCHEMA foo")]
    [InlineData("CREATE DATABASE foo")]
    [InlineData("CREATE INDEX i ON public.users (id)")]
    [InlineData("CREATE VIEW v AS SELECT 1")]
    [InlineData("CREATE FUNCTION f() RETURNS void LANGUAGE sql AS $$ SELECT 1 $$")]
    [InlineData("CREATE TRIGGER t BEFORE INSERT ON public.users EXECUTE PROCEDURE f()")]
    [InlineData("CREATE TYPE t AS ENUM ('a','b')")]
    [InlineData("CREATE SEQUENCE s")]
    [InlineData("DROP TABLE public.users")]
    [InlineData("DROP SCHEMA public")]
    [InlineData("DROP DATABASE foo")]
    [InlineData("DROP INDEX i")]
    [InlineData("DROP FUNCTION f()")]
    [InlineData("ALTER TABLE public.users ADD COLUMN x int")]
    [InlineData("ALTER SCHEMA public RENAME TO p")]
    [InlineData("ALTER DATABASE foo RENAME TO bar")]
    public void Rejects_object_ddl(string sql)
    {
        var result = _validator.Validate(sql);
        Assert.False(result.IsAllowed);
        Assert.Null(result.Category);
    }

    // ---------- Rejected: maintenance/admin ----------

    [Theory]
    [InlineData("VACUUM")]
    [InlineData("VACUUM public.users")]
    [InlineData("REINDEX TABLE public.users")]
    [InlineData("CLUSTER public.users")]
    [InlineData("ANALYZE public.users")]
    [InlineData("ALTER SYSTEM SET work_mem = '64MB'")]
    [InlineData("SET search_path = public")]
    [InlineData("RESET search_path")]
    [InlineData("DISCARD ALL")]
    [InlineData("LISTEN ch")]
    [InlineData("NOTIFY ch")]
    [InlineData("CHECKPOINT")]
    public void Rejects_maintenance_and_admin(string sql)
    {
        var result = _validator.Validate(sql);
        Assert.False(result.IsAllowed);
    }

    // ---------- Rejected: SELECT from non-system tables ----------

    [Theory]
    [InlineData("SELECT * FROM public.users")]
    [InlineData("SELECT * FROM users")]
    [InlineData("SELECT * FROM my_schema.my_table")]
    [InlineData("SELECT pg_terminate_backend(123)")]
    public void Rejects_select_from_non_system_or_function_calls(string sql)
    {
        var result = _validator.Validate(sql);
        Assert.False(result.IsAllowed);
    }

    // ---------- Multi-statement defense ----------

    [Theory]
    [InlineData("GRANT SELECT ON public.users TO r; DROP TABLE public.users")]
    [InlineData("CREATE ROLE foo; CREATE ROLE bar")]
    [InlineData("SELECT * FROM pg_roles; DROP DATABASE x")]
    public void Rejects_multi_statement(string sql)
    {
        var result = _validator.Validate(sql);
        Assert.False(result.IsAllowed);
        Assert.NotNull(result.RejectionReason);
        Assert.Contains("multi", result.RejectionReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Allows_trailing_semicolon()
    {
        var result = _validator.Validate("CREATE ROLE foo;");
        Assert.True(result.IsAllowed, result.RejectionReason);
        Assert.Equal(SqlStatementCategory.RoleManagement, result.Category);
    }

    [Fact]
    public void Allows_semicolon_inside_string_literal()
    {
        var result = _validator.Validate("CREATE ROLE foo PASSWORD 'a;b'");
        Assert.True(result.IsAllowed, result.RejectionReason);
    }

    // ---------- Comment handling ----------

    [Fact]
    public void Strips_line_comment_before_classification()
    {
        var result = _validator.Validate("-- evil drop comment\nCREATE ROLE foo");
        Assert.True(result.IsAllowed, result.RejectionReason);
    }

    [Fact]
    public void Strips_block_comment_before_classification()
    {
        var result = _validator.Validate("/* DROP TABLE x */ CREATE ROLE foo");
        Assert.True(result.IsAllowed, result.RejectionReason);
    }

    [Fact]
    public void Comment_does_not_smuggle_a_second_statement()
    {
        var result = _validator.Validate("GRANT SELECT ON pg_roles TO r --;\n; DROP TABLE x");
        Assert.False(result.IsAllowed);
    }

    // ---------- Whitespace, case, null/empty ----------

    [Theory]
    [InlineData("create role foo")]
    [InlineData("Create Role Foo")]
    [InlineData("   GRANT SELECT ON pg_roles TO r")]
    [InlineData("\nGRANT\tSELECT ON pg_roles TO r")]
    public void Is_case_and_whitespace_insensitive(string sql)
    {
        var result = _validator.Validate(sql);
        Assert.True(result.IsAllowed, result.RejectionReason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t  ")]
    [InlineData(";")]
    [InlineData("-- only a comment")]
    [InlineData("/* only block */")]
    public void Rejects_empty_or_whitespace_or_comment_only(string sql)
    {
        var result = _validator.Validate(sql);
        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void Throws_on_null()
    {
        Assert.Throws<ArgumentNullException>(() => _validator.Validate(null!));
    }

    // ---------- Default-deny smoke test ----------

    [Theory]
    [InlineData("GIBBERISH")]
    [InlineData("EXPLAIN SELECT 1")]
    [InlineData("BEGIN")]
    [InlineData("COMMIT")]
    [InlineData("ROLLBACK")]
    [InlineData("SAVEPOINT s")]
    public void Default_denies_unknown_or_unsupported(string sql)
    {
        var result = _validator.Validate(sql);
        Assert.False(result.IsAllowed);
    }

    // ---------- WITH / CTE support (Iter 5) ----------

    [Theory]
    [InlineData("WITH a AS (SELECT oid FROM pg_catalog.pg_roles) SELECT * FROM a")]
    [InlineData("WITH a AS (SELECT oid FROM pg_roles), b AS (SELECT oid FROM pg_namespace) SELECT * FROM a JOIN b ON a.oid = b.oid")]
    [InlineData("WITH RECURSIVE t AS (SELECT 1 FROM pg_catalog.pg_database) SELECT * FROM t")]
    public void Allows_select_with_cte_when_all_sources_are_catalogs_or_aliases(string sql)
    {
        var result = _validator.Validate(sql);
        Assert.True(result.IsAllowed, result.RejectionReason);
        Assert.Equal(SqlStatementCategory.ReadMetadata, result.Category);
    }

    [Theory]
    [InlineData("WITH evil AS (SELECT * FROM users) SELECT * FROM evil")]
    [InlineData("WITH a AS (SELECT 1 FROM pg_roles) SELECT * FROM customer_data")]
    public void Rejects_cte_with_user_table_source(string sql)
    {
        var result = _validator.Validate(sql);
        Assert.False(result.IsAllowed);
    }
}
