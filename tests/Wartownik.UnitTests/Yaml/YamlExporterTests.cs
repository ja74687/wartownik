using Wartownik.Connections;
using Wartownik.Yaml;

namespace Wartownik.UnitTests.Yaml;

public class YamlExporterTests
{
    private static ConnectionProfile SampleProfile() =>
        ConnectionProfile.Create(
            displayName: "Test",
            host: "localhost",
            port: 5432,
            database: "postgres",
            username: "alice",
            sslMode: PostgresSslMode.Disable);

    [Fact]
    public void BuildYaml_with_no_roles_emits_empty_roles_map()
    {
        var yaml = YamlExporter.BuildYaml(
            SampleProfile(),
            "mydb",
            Array.Empty<(RoleSummary, IReadOnlyList<SchemaGrantSummary>)>());

        Assert.Contains("database: \"mydb\"", yaml);
        Assert.Contains("roles: {}", yaml);
    }

    [Fact]
    public void BuildYaml_omits_schemas_with_no_privileges()
    {
        var alice = new RoleSummary("alice", IsSuperuser: false, CanCreateDb: false, CanCreateRole: false, CanLogin: true);
        var grants = new IReadOnlyList<SchemaGrantSummary>[]
        {
            new SchemaGrantSummary[]
            {
                // public has nothing — should be skipped
                new("public", false, false, false, false, false, false),
                // app has USAGE + SELECT — should appear
                new("app", true, false, true, false, false, false),
            },
        };

        var yaml = YamlExporter.BuildYaml(
            SampleProfile(),
            "mydb",
            new[] { (alice, (IReadOnlyList<SchemaGrantSummary>)grants[0]) });

        Assert.Contains("\"alice\":", yaml);
        Assert.Contains("\"app\":", yaml);
        Assert.DoesNotContain("\"public\":", yaml);
        Assert.Contains("- USAGE", yaml);
        Assert.Contains("- SELECT", yaml);
        Assert.DoesNotContain("- CREATE", yaml);
        Assert.DoesNotContain("- INSERT", yaml);
    }

    [Fact]
    public void BuildYaml_role_with_zero_grants_keeps_empty_schemas_block()
    {
        var bob = new RoleSummary("bob", false, false, false, true);
        var emptyGrants = new SchemaGrantSummary[]
        {
            new("public", false, false, false, false, false, false),
        };

        var yaml = YamlExporter.BuildYaml(
            SampleProfile(),
            "mydb",
            new[] { (bob, (IReadOnlyList<SchemaGrantSummary>)emptyGrants) });

        Assert.Contains("\"bob\":", yaml);
        Assert.Contains("schemas: {}", yaml);
    }

    [Fact]
    public void BuildYaml_orders_roles_and_schemas_alphabetically()
    {
        var bob = new RoleSummary("bob", false, false, false, true);
        var alice = new RoleSummary("alice", false, false, false, true);
        var bobGrants = new SchemaGrantSummary[] { new("zeta", true, false, false, false, false, false) };
        var aliceGrants = new SchemaGrantSummary[]
        {
            new("zeta", true, false, false, false, false, false),
            new("alpha", true, false, false, false, false, false),
        };

        var yaml = YamlExporter.BuildYaml(
            SampleProfile(),
            "mydb",
            new[]
            {
                (bob, (IReadOnlyList<SchemaGrantSummary>)bobGrants),
                (alice, (IReadOnlyList<SchemaGrantSummary>)aliceGrants),
            });

        var aliceIdx = yaml.IndexOf("\"alice\":", StringComparison.Ordinal);
        var bobIdx = yaml.IndexOf("\"bob\":", StringComparison.Ordinal);
        Assert.True(aliceIdx > 0 && bobIdx > 0 && aliceIdx < bobIdx, "alice should appear before bob");

        var alphaIdx = yaml.IndexOf("\"alpha\":", StringComparison.Ordinal);
        var zetaIdx = yaml.IndexOf("\"zeta\":", StringComparison.Ordinal);
        Assert.True(alphaIdx > 0 && zetaIdx > 0 && alphaIdx < zetaIdx, "alpha should appear before zeta within alice");
    }

    [Fact]
    public void BuildYaml_emits_all_six_privileges_when_all_granted()
    {
        var alice = new RoleSummary("alice", false, false, false, true);
        var fullGrants = new SchemaGrantSummary[]
        {
            new("app", Usage: true, Create: true, Select: true, Insert: true, Update: true, Delete: true),
        };

        var yaml = YamlExporter.BuildYaml(
            SampleProfile(),
            "mydb",
            new[] { (alice, (IReadOnlyList<SchemaGrantSummary>)fullGrants) });

        foreach (var priv in new[] { "USAGE", "CREATE", "SELECT", "INSERT", "UPDATE", "DELETE" })
            Assert.Contains("- " + priv, yaml);
    }

    [Fact]
    public void BuildYaml_quotes_identifiers_with_special_characters()
    {
        var weirdName = new RoleSummary("user.with-dot", false, false, false, true);
        var grants = new SchemaGrantSummary[]
        {
            new("schema with spaces", true, false, false, false, false, false),
        };

        var yaml = YamlExporter.BuildYaml(
            SampleProfile(),
            "db with spaces",
            new[] { (weirdName, (IReadOnlyList<SchemaGrantSummary>)grants) });

        Assert.Contains("database: \"db with spaces\"", yaml);
        Assert.Contains("\"user.with-dot\":", yaml);
        Assert.Contains("\"schema with spaces\":", yaml);
    }
}
