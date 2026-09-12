using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace Soenneker.Atomics.Resources.Tests;

public sealed class ResourceRegressionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Dispose_during_creation_or_reset_cleans_up_once(bool reset)
    {
        using var entered = new ManualResetEventSlim();
        using var proceed = new ManualResetEventSlim();
        int cleanups = 0;
        var resource = new AtomicResource<object>(() =>
        {
            entered.Set();
            if (!proceed.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException();
            return new object();
        }, _ => { Interlocked.Increment(ref cleanups); return default; });

        Task worker = Task.Run(async () =>
        {
            if (reset)
                await resource.Reset();
            else
                _ = resource.GetOrCreate();
        });
        try
        {
            await Assert.That(entered.Wait(TimeSpan.FromSeconds(5))).IsTrue();
            await resource.DisposeAsync();
        }
        finally
        {
            proceed.Set();
        }
        await worker.WaitAsync(TimeSpan.FromSeconds(5));
        await resource.DisposeAsync();
        await Assert.That(cleanups).IsEqualTo(1);
        await Assert.That(resource.TryGet()).IsNull();
        await Assert.That(resource.GetOrCreate()).IsNull();
    }

    [Test]
    public async Task Concurrent_reset_and_dispose_clean_up_every_candidate_once()
    {
        var created = new ConcurrentBag<object>();
        var cleaned = new ConcurrentDictionary<object, int>();
        var resource = new AtomicResource<object>(() =>
        {
            var value = new object();
            created.Add(value);
            return value;
        }, value => { cleaned.AddOrUpdate(value, 1, static (_, count) => count + 1); return default; });
        _ = resource.GetOrCreate();
        Task[] workers = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            for (int i = 0; i < 1000; i++)
                await resource.Reset();
        })).ToArray();
        await Task.Yield();
        await resource.DisposeAsync();
        await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(cleaned.Count).IsEqualTo(created.Count);
        await Assert.That(cleaned.Values.All(static count => count == 1)).IsTrue();
        await Assert.That(resource.TryGet()).IsNull();
    }

    [Test]
    public async Task Completed_source_backed_teardown_is_consumed()
    {
        var source = new CleanupSource();
        var resource = new AtomicResource<object>(static () => new object(), _ => source.Task);
        _ = resource.GetOrCreate();
        await resource.DisposeAsync();
        await Assert.That(source.Consumptions).IsEqualTo(1);
    }

    [Test]
    public async Task Synchronous_disposal_waits_for_async_teardown()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resource = new AtomicResource<object>(static () => new object(), async _ =>
        {
            entered.SetResult();
            await release.Task;
        });
        _ = resource.GetOrCreate();
        Task disposal = Task.Run(resource.Dispose);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(disposal.IsCompleted).IsFalse();
        release.SetResult();
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class CleanupSource : IValueTaskSource
    {
        public int Consumptions;
        public ValueTask Task => new(this, 0);
        public void GetResult(short token) => Consumptions++;
        public ValueTaskSourceStatus GetStatus(short token) => ValueTaskSourceStatus.Succeeded;
        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) => throw new InvalidOperationException();
    }

    [Test]
    public async Task Synchronous_disposal_waits_for_a_pending_value_task_source()
    {
        var source = new PendingCleanupSource();
        var resource = new AtomicResource<object>(static () => new object(), _ => source.Task);
        _ = resource.GetOrCreate();
        Task disposal = System.Threading.Tasks.Task.Run(resource.Dispose);
        try
        {
            await System.Threading.Tasks.Task.WhenAny(disposal, source.Registered.Task).WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(disposal.IsCompleted).IsFalse();
        }
        finally
        {
            source.Complete();
        }
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(source.Consumptions).IsEqualTo(1);
    }

    private sealed class PendingCleanupSource : IValueTaskSource
    {
        private ManualResetValueTaskSourceCore<bool> _core = new() { RunContinuationsAsynchronously = true };
        public readonly TaskCompletionSource Registered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Consumptions;
        public ValueTask Task => new(this, _core.Version);
        public void Complete() => _core.SetResult(true);
        public void GetResult(short token)
        {
            _core.GetResult(token);
            Consumptions++;
        }
        public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);
        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            _core.OnCompleted(continuation, state, token, flags);
            Registered.TrySetResult();
        }
    }
}
