using System;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Atomics.Resources.Abstract;
using Soenneker.Extensions.ValueTask;

namespace Soenneker.Atomics.Resources;

///<inheritdoc cref="IAtomicResource{T}"/>
public sealed class AtomicResource<T> : IAtomicResource<T> where T : class
{
    private readonly Func<T> _factory;
    private readonly Func<T, ValueTask> _teardown;
    private T? _value;
    private volatile bool _disposed;

    public AtomicResource(Func<T> factory, Func<T, ValueTask> teardown)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _teardown = teardown ?? throw new ArgumentNullException(nameof(teardown));
    }

    public bool IsDisposed => _disposed;

    public T? GetOrCreate()
    {
        if (_disposed)
            return null;

        T? existing = Volatile.Read(ref _value);
        if (existing is not null)
            return existing;

        T created = _factory();
        T? raced = Interlocked.CompareExchange(ref _value, created, null);

        if (raced is null)
        {
            // We published 'created'; check for a dispose race.
            if (_disposed)
            {
                Interlocked.Exchange(ref _value, null);
                _ = _teardown(created); // best-effort cleanup (cannot block or await here)
                return null;
            }

            return created;
        }

        // Lost the race; tear down our extra
        _ = _teardown(created);
        return raced;
    }

    public T? TryGet() => Volatile.Read(ref _value);

    public async ValueTask Reset()
    {
        if (_disposed)
            return;

        T fresh = _factory();
        T? old = Interlocked.Exchange(ref _value, fresh);

        if (old is null)
            return;

        try
        {
            await _teardown(old).NoSync();
        }
        catch
        {
            /* ignore */
        }
    }

    /// <summary>
    /// Asynchronously disposes the current resource (if any) using the async teardown.
    /// Safe to call multiple times.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        T? old = Interlocked.Exchange(ref _value, null);

        if (old is null)
            return;

        try
        {
            await _teardown(old).NoSync();
        }
        catch
        {
            /* ignore */
        }
    }

    /// <summary>
    /// Synchronously disposes the current resource (if any) by blocking on the teardown ValueTask.
    /// Use when you need deterministic synchronous cleanup (e.g., using-statement).
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        T? old = Interlocked.Exchange(ref _value, null);

        if (old is null)
            return;

        try
        {
            // Block synchronously without allocating a Task if possible.
            ValueTask vt = _teardown(old);
            vt.GetAwaiter().GetResult();
        }
        catch
        {
            /* ignore */
        }
    }
}
