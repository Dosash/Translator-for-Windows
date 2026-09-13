using Translator.Offline;

namespace Translator.Tests.Offline;

public class ModelCacheTests
{
    [Fact]
    public void LoadsEachModelOnce()
    {
        var loads = 0;
        using var cache = new ModelCache<FakeModel>(2, id => { loads++; return new FakeModel(id); });

        using (var first = cache.Acquire("a"))
        using (var second = cache.Acquire("a"))
        {
            Assert.Same(first.Value, second.Value);
        }

        Assert.Equal(1, loads);
    }

    [Fact]
    public void EvictsTheLeastRecentlyUsedModelBeyondCapacity()
    {
        var time = new ManualTime();
        using var cache = new ModelCache<FakeModel>(2, id => new FakeModel(id), time);
        FakeModel a, b;
        using (var lease = cache.Acquire("a")) { a = lease.Value; }
        time.Advance(1);
        using (var lease = cache.Acquire("b")) { b = lease.Value; }
        time.Advance(1);
        using (cache.Acquire("a")) { }
        time.Advance(1);
        using (cache.Acquire("c")) { }

        Assert.True(b.Disposed);
        Assert.False(a.Disposed);
        Assert.True(cache.Contains("a"));
        Assert.False(cache.Contains("b"));
    }

    [Fact]
    public void EvictedModelInUseIsDisposedAfterRelease()
    {
        using var cache = new ModelCache<FakeModel>(2, id => new FakeModel(id));
        var lease = cache.Acquire("a");
        cache.Evict("a");
        Assert.False(lease.Value.Disposed);

        lease.Dispose();
        Assert.True(lease.Value.Disposed);
        lease.Dispose();
    }

    [Fact]
    public void TrimIdleReleasesUnusedModels()
    {
        var time = new ManualTime();
        using var cache = new ModelCache<FakeModel>(3, id => new FakeModel(id), time);
        FakeModel idle;
        using (var lease = cache.Acquire("idle")) { idle = lease.Value; }
        using var busy = cache.Acquire("busy");
        time.Advance(600);

        cache.TrimIdle(TimeSpan.FromMinutes(5));

        Assert.True(idle.Disposed);
        Assert.False(busy.Value.Disposed);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void DisposeReleasesModels()
    {
        var cache = new ModelCache<FakeModel>(2, id => new FakeModel(id));
        FakeModel model;
        using (var lease = cache.Acquire("a")) { model = lease.Value; }
        cache.Dispose();
        Assert.True(model.Disposed);
        Assert.Throws<ObjectDisposedException>(() => cache.Acquire("a"));
    }

    public sealed class FakeModel(string id) : IDisposable
    {
        public string Id { get; } = id;
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class ManualTime : TimeProvider
    {
        private long _seconds;

        public override long TimestampFrequency => 1;

        public override long GetTimestamp() => _seconds;

        public void Advance(long seconds) => _seconds += seconds;
    }
}
