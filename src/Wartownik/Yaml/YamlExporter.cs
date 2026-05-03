using System.Text;
using Wartownik.Connections;

namespace Wartownik.Yaml;

public sealed class YamlExporter : IYamlExporter
{
    private readonly IPostgresMetadataService _metadata;
    private readonly IPostgresGrantService _grants;

    public YamlExporter(IPostgresMetadataService metadata, IPostgresGrantService grants)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(grants);
        _metadata = metadata;
        _grants = grants;
    }

    public async Task<string> ExportAsync(
        ConnectionProfile profile,
        string profilePassword,
        string databaseName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(profilePassword);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        var roles = await _metadata
            .ListRolesAsync(profile, profilePassword, cancellationToken)
            .ConfigureAwait(false);

        // Only login roles end up in the YAML — group roles and superusers are out of scope
        // (superusers bypass everything anyway, group roles need a separate "groups" section
        // we'll add when membership management ships).
        var loginRoles = roles
            .Where(r => r.CanLogin && !r.IsSuperuser)
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Per-role grant fetch — happens serially so we don't hammer the server with N
        // parallel sessions. Roles with all-zero grants on every schema get an empty section
        // so the export still records they exist as a known target.
        var grantsByRole = new List<(RoleSummary Role, IReadOnlyList<SchemaGrantSummary> Grants)>();
        foreach (var role in loginRoles)
        {
            var grants = await _grants
                .ListSchemaGrantsAsync(profile, profilePassword, databaseName, role.Name, cancellationToken)
                .ConfigureAwait(false);
            grantsByRole.Add((role, grants));
        }

        return BuildYaml(profile, databaseName, grantsByRole);
    }

    /// <summary>
    /// Render the snapshot. Indent-sensitive YAML — written by hand because the dependency
    /// surface for a full YAML library isn't worth it for this small, controlled shape.
    /// </summary>
    internal static string BuildYaml(
        ConnectionProfile profile,
        string databaseName,
        IReadOnlyList<(RoleSummary Role, IReadOnlyList<SchemaGrantSummary> Grants)> grantsByRole)
    {
        var sb = new StringBuilder();
        sb.Append("# Wartownik export — ");
        sb.Append(DateTimeOffset.UtcNow.ToString("o"));
        sb.AppendLine();
        sb.Append("# profile: ").AppendLine(EscapeComment(profile.DisplayName));
        sb.AppendLine();

        sb.Append("database: ").AppendLine(QuoteIfNeeded(databaseName));

        if (grantsByRole.Count == 0)
        {
            sb.AppendLine("roles: {}");
            return sb.ToString();
        }

        sb.AppendLine("roles:");
        // Sort by role name so the YAML is stable and diffable across exports — even if the
        // caller didn't sort. ExportAsync does sort, but BuildYaml is also called from tests.
        var sorted = grantsByRole
            .OrderBy(t => t.Role.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var (role, schemaGrants) in sorted)
        {
            sb.Append("  ").Append(QuoteIfNeeded(role.Name)).AppendLine(":");

            var schemasWithGrants = schemaGrants
                .Where(s => s.Usage || s.Create || s.Select || s.Insert || s.Update || s.Delete)
                .OrderBy(s => s.SchemaName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (schemasWithGrants.Count == 0)
            {
                sb.AppendLine("    schemas: {}");
                continue;
            }

            sb.AppendLine("    schemas:");
            foreach (var schema in schemasWithGrants)
            {
                sb.Append("      ").Append(QuoteIfNeeded(schema.SchemaName)).AppendLine(":");
                sb.AppendLine("        privileges:");
                AppendPriv(sb, "USAGE", schema.Usage);
                AppendPriv(sb, "CREATE", schema.Create);
                AppendPriv(sb, "SELECT", schema.Select);
                AppendPriv(sb, "INSERT", schema.Insert);
                AppendPriv(sb, "UPDATE", schema.Update);
                AppendPriv(sb, "DELETE", schema.Delete);
            }
        }

        return sb.ToString();
    }

    private static void AppendPriv(StringBuilder sb, string name, bool granted)
    {
        if (granted)
            sb.Append("          - ").AppendLine(name);
    }

    /// <summary>
    /// YAML scalars that contain colons / spaces / start with reserved tokens need quoting.
    /// We err on the side of always quoting identifiers so the output round-trips with
    /// case-sensitive Postgres names.
    /// </summary>
    private static string QuoteIfNeeded(string value)
    {
        // Always quote — keeps Postgres identifiers (case-sensitive, with possible spaces) safe.
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private static string EscapeComment(string value) =>
        value.Replace("\r", "").Replace("\n", " ");
}
