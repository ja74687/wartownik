using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wartownik.Audit;

/// <summary>
/// JSONL (one JSON object per line) append-only audit log. Chosen over a single JSON array because:
/// - Append is a single FileStream write — no read-modify-rewrite, so concurrent appends are safer.
/// - File can grow into MB without us re-parsing the whole array on every Append.
/// - Reading newest-first is just "tail the file"; we slurp everything for now since logs stay small,
///   but the format is ready for a future bounded reader.
/// </summary>
public sealed class JsonAuditLogStore : IAuditLogStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false,
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public JsonAuditLogStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    public async Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var json = JsonSerializer.Serialize(entry, SerializerOptions);
        var line = json + Environment.NewLine;

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureDirectory();
            await File.AppendAllTextAsync(_filePath, line, Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<IReadOnlyList<AuditEntry>> ListAsync(
        Guid? profileId = null,
        string? databaseName = null,
        int max = 200,
        CancellationToken cancellationToken = default)
    {
        if (max <= 0)
            return Array.Empty<AuditEntry>();
        if (!File.Exists(_filePath))
            return Array.Empty<AuditEntry>();

        var lines = await File.ReadAllLinesAsync(_filePath, Encoding.UTF8, cancellationToken)
            .ConfigureAwait(false);

        // Walk in reverse so we naturally take "newest first" without sorting the whole file.
        var result = new List<AuditEntry>(capacity: Math.Min(max, lines.Length));
        for (int i = lines.Length - 1; i >= 0 && result.Count < max; i--)
        {
            var line = lines[i].Trim();
            if (line.Length == 0)
                continue;

            AuditEntry? entry;
            try
            {
                entry = JsonSerializer.Deserialize<AuditEntry>(line, SerializerOptions);
            }
            catch (JsonException)
            {
                // A truncated tail line shouldn't kill the rest of the log — skip and keep reading.
                continue;
            }

            if (entry is null)
                continue;
            if (profileId.HasValue && entry.ProfileId != profileId.Value)
                continue;
            if (databaseName is not null
                && !string.Equals(entry.DatabaseName, databaseName, StringComparison.Ordinal))
                continue;

            result.Add(entry);
        }
        return result;
    }

    private void EnsureDirectory()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }
}
