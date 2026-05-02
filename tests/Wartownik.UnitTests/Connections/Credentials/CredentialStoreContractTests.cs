using Wartownik.Connections.Credentials;

namespace Wartownik.UnitTests.Connections.Credentials;

public abstract class CredentialStoreContractTests : IDisposable
{
    protected ICredentialStore Store { get; }
    protected string ServiceName { get; }
    private readonly List<string> _keysToCleanup = new();

    protected CredentialStoreContractTests()
    {
        ServiceName = $"WartownikTest-{Guid.NewGuid():N}";
        if (ShouldSkip(out _))
        {
            Store = null!;
            return;
        }
        Store = Create(ServiceName);
    }

    protected abstract bool ShouldSkip(out string reason);
    protected abstract ICredentialStore Create(string serviceName);

    protected void Track(string key) => _keysToCleanup.Add(key);

    public virtual void Dispose()
    {
        if (Store is null) return;
        foreach (var key in _keysToCleanup)
        {
            try { Store.Remove(key); }
            catch { /* best-effort cleanup */ }
        }
        GC.SuppressFinalize(this);
    }

    [SkippableFact]
    public void Get_returns_null_when_key_unknown()
    {
        Skip.If(ShouldSkip(out var reason), reason);
        Assert.Null(Store.Get($"unknown-{Guid.NewGuid():N}"));
    }

    [SkippableFact]
    public void Set_then_Get_round_trips_secret()
    {
        Skip.If(ShouldSkip(out var reason), reason);
        var key = $"key-{Guid.NewGuid():N}";
        Track(key);

        Store.Set(key, "s3cret-value!@#$%^&*()_=+");

        Assert.Equal("s3cret-value!@#$%^&*()_=+", Store.Get(key));
    }

    [SkippableFact]
    public void Set_overwrites_existing_secret_under_same_key()
    {
        Skip.If(ShouldSkip(out var reason), reason);
        var key = $"key-{Guid.NewGuid():N}";
        Track(key);

        Store.Set(key, "first");
        Store.Set(key, "second");

        Assert.Equal("second", Store.Get(key));
    }

    [SkippableFact]
    public void Set_handles_unicode_secret()
    {
        Skip.If(ShouldSkip(out var reason), reason);
        var key = $"key-{Guid.NewGuid():N}";
        Track(key);

        const string unicode = "zażółć gęślą jaźń — 日本語 — 🔐";
        Store.Set(key, unicode);

        Assert.Equal(unicode, Store.Get(key));
    }

    [SkippableFact]
    public void Set_accepts_empty_secret()
    {
        Skip.If(ShouldSkip(out var reason), reason);
        var key = $"key-{Guid.NewGuid():N}";
        Track(key);

        Store.Set(key, "");

        Assert.Equal("", Store.Get(key));
    }

    [SkippableFact]
    public void Remove_returns_true_when_key_existed()
    {
        Skip.If(ShouldSkip(out var reason), reason);
        var key = $"key-{Guid.NewGuid():N}";
        Track(key);
        Store.Set(key, "value");

        var removed = Store.Remove(key);

        Assert.True(removed);
        Assert.Null(Store.Get(key));
    }

    [SkippableFact]
    public void Remove_returns_false_when_key_missing()
    {
        Skip.If(ShouldSkip(out var reason), reason);
        var removed = Store.Remove($"never-set-{Guid.NewGuid():N}");
        Assert.False(removed);
    }

    [SkippableFact]
    public void Multiple_keys_isolate_their_secrets()
    {
        Skip.If(ShouldSkip(out var reason), reason);
        var k1 = $"key-{Guid.NewGuid():N}";
        var k2 = $"key-{Guid.NewGuid():N}";
        Track(k1);
        Track(k2);

        Store.Set(k1, "alpha");
        Store.Set(k2, "beta");

        Assert.Equal("alpha", Store.Get(k1));
        Assert.Equal("beta", Store.Get(k2));
    }

    [SkippableTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Get_throws_on_blank_key(string? key)
    {
        Skip.If(ShouldSkip(out var reason), reason);
        Assert.ThrowsAny<ArgumentException>(() => Store.Get(key!));
    }

    [SkippableTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Set_throws_on_blank_key(string? key)
    {
        Skip.If(ShouldSkip(out var reason), reason);
        Assert.ThrowsAny<ArgumentException>(() => Store.Set(key!, "value"));
    }

    [SkippableTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Remove_throws_on_blank_key(string? key)
    {
        Skip.If(ShouldSkip(out var reason), reason);
        Assert.ThrowsAny<ArgumentException>(() => Store.Remove(key!));
    }

    [SkippableFact]
    public void Set_throws_on_null_secret()
    {
        Skip.If(ShouldSkip(out var reason), reason);
        Assert.Throws<ArgumentNullException>(() => Store.Set("key", null!));
    }
}
