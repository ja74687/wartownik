using Wartownik.Connections;
using Wartownik.Sql;

namespace Wartownik.UnitTests.Connections;

public class PostgresRoleMembershipServiceTests
{
    private static IReadOnlyList<string> Build(string member, params RoleMembershipChange[] changes) =>
        PostgresRoleMembershipService.BuildStatements(member, changes);

    [Fact]
    public void Grant_builds_grant_group_to_member()
    {
        var sql = Build("alice", new RoleMembershipChange("devs", GrantOperation.Grant));

        Assert.Equal("GRANT \"devs\" TO \"alice\"", Assert.Single(sql));
    }

    [Fact]
    public void Revoke_builds_revoke_group_from_member()
    {
        var sql = Build("alice", new RoleMembershipChange("devs", GrantOperation.Revoke));

        Assert.Equal("REVOKE \"devs\" FROM \"alice\"", Assert.Single(sql));
    }

    [Fact]
    public void Statements_keep_the_order_of_the_changes()
    {
        var sql = Build(
            "alice",
            new RoleMembershipChange("devs", GrantOperation.Grant),
            new RoleMembershipChange("analysts", GrantOperation.Revoke));

        Assert.Equal(2, sql.Count);
        Assert.StartsWith("GRANT", sql[0]);
        Assert.StartsWith("REVOKE", sql[1]);
    }

    [Fact]
    public void Identifiers_with_quotes_are_escaped_on_both_sides()
    {
        var sql = Build("ali\"ce", new RoleMembershipChange("de\"vs", GrantOperation.Grant));

        Assert.Equal("GRANT \"de\"\"vs\" TO \"ali\"\"ce\"", Assert.Single(sql));
    }

    [Fact]
    public void No_changes_produce_no_statements()
    {
        Assert.Empty(Build("alice"));
    }

    [Theory]
    [InlineData("devs", GrantOperation.Grant)]
    [InlineData("devs", GrantOperation.Revoke)]
    [InlineData("team-analytics", GrantOperation.Grant)]
    [InlineData("Grupa Analityków", GrantOperation.Grant)]
    [InlineData("ro\"le", GrantOperation.Revoke)]
    public void Generated_statements_pass_the_sql_whitelist(string group, GrantOperation operation)
    {
        // Membership SQL goes through the same validator as everything else — if a role name could
        // produce a statement the whitelist rejects, the feature would break at runtime.
        var validator = new PostgresSqlStatementValidator();
        var sql = Assert.Single(Build("alice", new RoleMembershipChange(group, operation)));

        var result = validator.Validate(sql);

        Assert.True(result.IsAllowed, $"Validator rejected: {sql}");
        Assert.Equal(SqlStatementCategory.GrantRevoke, result.Category);
    }

    [Fact]
    public void A_role_name_containing_a_semicolon_is_rejected_by_the_validator()
    {
        // Known limitation, not specific to membership: the validator strips '...' literals but
        // not "..." identifiers, so a semicolon inside a quoted name reads as a second statement.
        // It fails closed (the change is refused, never mis-executed) — the quoting itself is
        // correct, so this is a usability edge, not an injection hole. Documented so a future
        // identifier-aware validator has a test to flip.
        var validator = new PostgresSqlStatementValidator();
        var sql = Assert.Single(Build("alice", new RoleMembershipChange("we;ird", GrantOperation.Grant)));

        Assert.Equal("GRANT \"we;ird\" TO \"alice\"", sql); // the SQL itself is well-formed
        Assert.False(validator.Validate(sql).IsAllowed);    // but the whitelist refuses it
    }
}
