using Wartownik.Audit;

namespace Wartownik.UnitTests.Audit;

public class JsonAuditLogStoreTests : IDisposable
{
    private readonly string _tempFile;

    public JsonAuditLogStoreTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"wartownik-audit-test-{Guid.NewGuid()}.jsonl");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
            File.Delete(_tempFile);
    }

    private static AuditEntry Sample(
        Guid? profileId = null,
        string database = "mydb",
        string role = "alice",
        AuditOutcome outcome = AuditOutcome.Success,
        DateTimeOffset? when = null,
        IReadOnlyList<string>? statements = null) =>
        new(
            Id: Guid.NewGuid(),
            Timestamp: when ?? DateTimeOffset.UtcNow,
            ProfileId: profileId ?? Guid.NewGuid(),
            ProfileName: "Test profile",
            DatabaseName: database,
            TargetRoleName: role,
            Statements: statements ?? new[] { "GRANT USAGE ON SCHEMA \"app\" TO \"alice\"" },
            Outcome: outcome,
            ErrorMessage: null,
            Executor: "test@machine");

    [Fact]
    public async Task ListAsync_on_missing_file_returns_empty()
    {
        var store = new JsonAuditLogStore(_tempFile);
        var result = await store.ListAsync();
        Assert.Empty(result);
    }

    [Fact]
    public async Task AppendAsync_persists_then_ListAsync_round_trips_the_entry()
    {
        var store = new JsonAuditLogStore(_tempFile);
        var entry = Sample(role: "bob", database: "ordersdb");

        await store.AppendAsync(entry);

        var result = await store.ListAsync();
        var roundTripped = Assert.Single(result);
        Assert.Equal(entry.Id, roundTripped.Id);
        Assert.Equal("bob", roundTripped.TargetRoleName);
        Assert.Equal("ordersdb", roundTripped.DatabaseName);
        Assert.Equal(entry.Statements, roundTripped.Statements);
    }

    [Fact]
    public async Task ListAsync_returns_entries_newest_first()
    {
        var store = new JsonAuditLogStore(_tempFile);
        var older = Sample(when: DateTimeOffset.UtcNow.AddMinutes(-10));
        var newer = Sample(when: DateTimeOffset.UtcNow);

        await store.AppendAsync(older);
        await store.AppendAsync(newer);

        var result = await store.ListAsync();
        Assert.Equal(2, result.Count);
        // Reverse-iteration over the file naturally yields newer-appended first.
        Assert.Equal(newer.Id, result[0].Id);
        Assert.Equal(older.Id, result[1].Id);
    }

    [Fact]
    public async Task ListAsync_filters_by_profileId()
    {
        var store = new JsonAuditLogStore(_tempFile);
        var profile1 = Guid.NewGuid();
        var profile2 = Guid.NewGuid();
        await store.AppendAsync(Sample(profileId: profile1));
        await store.AppendAsync(Sample(profileId: profile2));
        await store.AppendAsync(Sample(profileId: profile1));

        var result = await store.ListAsync(profileId: profile1);
        Assert.Equal(2, result.Count);
        Assert.All(result, e => Assert.Equal(profile1, e.ProfileId));
    }

    [Fact]
    public async Task ListAsync_filters_by_databaseName()
    {
        var store = new JsonAuditLogStore(_tempFile);
        await store.AppendAsync(Sample(database: "mydb"));
        await store.AppendAsync(Sample(database: "audit"));
        await store.AppendAsync(Sample(database: "mydb"));

        var result = await store.ListAsync(databaseName: "mydb");
        Assert.Equal(2, result.Count);
        Assert.All(result, e => Assert.Equal("mydb", e.DatabaseName));
    }

    [Fact]
    public async Task ListAsync_caps_results_at_max()
    {
        var store = new JsonAuditLogStore(_tempFile);
        for (int i = 0; i < 10; i++)
            await store.AppendAsync(Sample(when: DateTimeOffset.UtcNow.AddSeconds(i)));

        var result = await store.ListAsync(max: 3);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task ListAsync_skips_corrupted_line_and_keeps_reading()
    {
        var store = new JsonAuditLogStore(_tempFile);
        await store.AppendAsync(Sample(role: "alice"));
        // Manually corrupt the file with a truncated line in the middle.
        await File.AppendAllTextAsync(_tempFile, "{\"this is not valid json" + Environment.NewLine);
        await store.AppendAsync(Sample(role: "bob"));

        var result = await store.ListAsync();
        Assert.Equal(2, result.Count);
        Assert.Equal("bob", result[0].TargetRoleName);
        Assert.Equal("alice", result[1].TargetRoleName);
    }

    [Fact]
    public async Task AppendAsync_creates_directory_if_missing()
    {
        var nested = Path.Combine(Path.GetTempPath(),
            $"wartownik-audit-test-{Guid.NewGuid()}",
            "nested",
            "audit.jsonl");
        try
        {
            var store = new JsonAuditLogStore(nested);
            await store.AppendAsync(Sample());
            Assert.True(File.Exists(nested));
        }
        finally
        {
            var dir = Path.GetDirectoryName(Path.GetDirectoryName(nested));
            if (dir is not null && Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
