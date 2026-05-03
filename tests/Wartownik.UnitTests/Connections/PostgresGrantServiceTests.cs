using Wartownik.Connections;
using Wartownik.Sql;

namespace Wartownik.UnitTests.Connections;

public class PostgresGrantServiceTests
{
    [Fact]
    public void BuildStatements_grant_usage_emits_single_grant_on_schema()
    {
        var changes = new[] { new GrantChange("app", GrantPrivilege.Usage, GrantOperation.Grant) };
        var sql = PostgresGrantService.BuildStatements("alice", changes);
        var stmt = Assert.Single(sql);
        Assert.Equal("GRANT USAGE ON SCHEMA \"app\" TO \"alice\"", stmt);
    }

    [Fact]
    public void BuildStatements_revoke_create_emits_revoke_from_schema()
    {
        var changes = new[] { new GrantChange("app", GrantPrivilege.Create, GrantOperation.Revoke) };
        var sql = PostgresGrantService.BuildStatements("alice", changes);
        var stmt = Assert.Single(sql);
        Assert.Equal("REVOKE CREATE ON SCHEMA \"app\" FROM \"alice\"", stmt);
    }

    [Fact]
    public void BuildStatements_grant_select_emits_two_statements_current_and_default()
    {
        var changes = new[] { new GrantChange("app", GrantPrivilege.Select, GrantOperation.Grant) };
        var sql = PostgresGrantService.BuildStatements("alice", changes);
        Assert.Equal(2, sql.Count);
        Assert.Equal("GRANT SELECT ON ALL TABLES IN SCHEMA \"app\" TO \"alice\"", sql[0]);
        Assert.Equal("ALTER DEFAULT PRIVILEGES IN SCHEMA \"app\" GRANT SELECT ON TABLES TO \"alice\"", sql[1]);
    }

    [Fact]
    public void BuildStatements_revoke_delete_emits_revoke_for_current_and_default()
    {
        var changes = new[] { new GrantChange("public", GrantPrivilege.Delete, GrantOperation.Revoke) };
        var sql = PostgresGrantService.BuildStatements("bob", changes);
        Assert.Equal(2, sql.Count);
        Assert.Equal("REVOKE DELETE ON ALL TABLES IN SCHEMA \"public\" FROM \"bob\"", sql[0]);
        Assert.Equal("ALTER DEFAULT PRIVILEGES IN SCHEMA \"public\" REVOKE DELETE ON TABLES FROM \"bob\"", sql[1]);
    }

    [Fact]
    public void BuildStatements_quotes_identifiers_with_embedded_quotes()
    {
        var changes = new[] { new GrantChange("we\"ird", GrantPrivilege.Usage, GrantOperation.Grant) };
        var sql = PostgresGrantService.BuildStatements("o\"ddly", changes);
        var stmt = Assert.Single(sql);
        Assert.Equal("GRANT USAGE ON SCHEMA \"we\"\"ird\" TO \"o\"\"ddly\"", stmt);
    }

    [Fact]
    public void BuildStatements_preserves_order_for_a_batch()
    {
        var changes = new[]
        {
            new GrantChange("app", GrantPrivilege.Usage, GrantOperation.Grant),
            new GrantChange("app", GrantPrivilege.Select, GrantOperation.Grant),
            new GrantChange("audit", GrantPrivilege.Delete, GrantOperation.Revoke),
        };
        var sql = PostgresGrantService.BuildStatements("alice", changes);
        // 1 (USAGE) + 2 (SELECT current + default) + 2 (DELETE current + default) = 5
        Assert.Equal(5, sql.Count);
        Assert.StartsWith("GRANT USAGE ON SCHEMA \"app\"", sql[0]);
        Assert.StartsWith("GRANT SELECT ON ALL TABLES IN SCHEMA \"app\"", sql[1]);
        Assert.StartsWith("ALTER DEFAULT PRIVILEGES IN SCHEMA \"app\" GRANT SELECT", sql[2]);
        Assert.StartsWith("REVOKE DELETE ON ALL TABLES IN SCHEMA \"audit\"", sql[3]);
        Assert.StartsWith("ALTER DEFAULT PRIVILEGES IN SCHEMA \"audit\" REVOKE DELETE", sql[4]);
    }

    [Fact]
    public void BuildStatements_empty_change_list_returns_empty()
    {
        var sql = PostgresGrantService.BuildStatements("alice", Array.Empty<GrantChange>());
        Assert.Empty(sql);
    }

    /// <summary>
    /// Every statement BuildStatements emits must pass the SQL validator — otherwise Apply will
    /// blow up at runtime when the validating session tries to execute a rejected statement.
    /// This is the contract between BuildStatements and the security guardrail at the session edge.
    /// </summary>
    [Fact]
    public void BuildStatements_all_emitted_statements_pass_the_sql_validator()
    {
        var validator = new PostgresSqlStatementValidator();
        var changes = new[]
        {
            new GrantChange("app", GrantPrivilege.Usage, GrantOperation.Grant),
            new GrantChange("app", GrantPrivilege.Create, GrantOperation.Revoke),
            new GrantChange("app", GrantPrivilege.Select, GrantOperation.Grant),
            new GrantChange("app", GrantPrivilege.Insert, GrantOperation.Grant),
            new GrantChange("audit", GrantPrivilege.Update, GrantOperation.Revoke),
            new GrantChange("audit", GrantPrivilege.Delete, GrantOperation.Grant),
        };

        foreach (var sql in PostgresGrantService.BuildStatements("alice", changes))
        {
            var result = validator.Validate(sql);
            Assert.True(result.IsAllowed, $"validator rejected: {sql} — {result.RejectionReason}");
        }
    }
}
