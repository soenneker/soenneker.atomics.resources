using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Atomics.Resources.Tests;

public sealed class ResourceLifecycleTests
{
    [Test]
    public async Task Null_factory_result_is_rejected_and_creation_can_retry()
    {
        int calls = 0;
        var resource = new AtomicResource<object>(() => ++calls == 1 ? null! : new object(), static _ => default);
        bool rejected = false;
        try { resource.GetOrCreate(); }
        catch (InvalidOperationException) { rejected = true; }
        await Assert.That(rejected).IsTrue();
        await Assert.That(resource.TryGet()).IsNull();
        await Assert.That(resource.IsDisposed).IsFalse();
        await Assert.That(resource.GetOrCreate()).IsNotNull();
        await resource.DisposeAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Failed_reset_preserves_current_resource(bool returnNull)
    {
        var expected = new object();
        int calls = 0;
        int cleanups = 0;
        var resource = new AtomicResource<object>(() =>
        {
            if (++calls == 1) return expected;
            return returnNull ? null! : throw new InvalidOperationException();
        }, _ => { cleanups++; return default; });
        resource.GetOrCreate();
        bool rejected = false;
        try { await resource.Reset(); }
        catch (InvalidOperationException) { rejected = true; }
        await Assert.That(rejected).IsTrue();
        await Assert.That(ReferenceEquals(resource.TryGet(), expected)).IsTrue();
        await Assert.That(cleanups).IsEqualTo(0);
        resource.Dispose();
        await Assert.That(cleanups).IsEqualTo(1);
    }

    [Test]
    public async Task Concurrent_creation_cleans_only_the_losing_candidate()
    {
        using var barrier = new Barrier(2);
        var cleaned = new ConcurrentBag<object>();
        var resource = new AtomicResource<object>(() =>
        {
            if (!barrier.SignalAndWait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return new object();
        }, value => { cleaned.Add(value); return default; });
        Task<object?> first = Task.Run(resource.GetOrCreate);
        Task<object?> second = Task.Run(resource.GetOrCreate);
        object?[] results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(ReferenceEquals(results[0], results[1])).IsTrue();
        await Assert.That(cleaned.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(cleaned.Single(), results[0])).IsFalse();
        await resource.DisposeAsync();
        await Assert.That(cleaned.Distinct().Count()).IsEqualTo(2);
    }

    [Test]
    public async Task Concurrent_sync_and_async_disposal_tears_down_once()
    {
        int cleanups = 0;
        var resource = new AtomicResource<object>(static () => new object(), _ =>
        {
            Interlocked.Increment(ref cleanups);
            return default;
        });
        resource.GetOrCreate();
        Task[] workers = Enumerable.Range(0, 16).Select(index => Task.Run(async () =>
        {
            if ((index & 1) == 0) resource.Dispose();
            else await resource.DisposeAsync();
        })).ToArray();
        await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(cleanups).IsEqualTo(1);
        await Assert.That(resource.IsDisposed).IsTrue();
        await Assert.That(resource.GetOrCreate()).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Teardown_failures_do_not_escape_disposal(bool asynchronous)
    {
        var resource = new AtomicResource<object>(static () => new object(), asynchronous
            ? static async _ => { await Task.Yield(); throw new InvalidOperationException(); }
            : static _ => throw new InvalidOperationException());
        resource.GetOrCreate();
        resource.Dispose();
        await Assert.That(resource.IsDisposed).IsTrue();
        await resource.DisposeAsync();
    }
}
