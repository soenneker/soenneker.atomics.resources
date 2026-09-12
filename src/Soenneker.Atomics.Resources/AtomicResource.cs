using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Atomics.Resources.Abstract;
using Soenneker.Extensions.ValueTask;

namespace Soenneker.Atomics.Resources;

public sealed class AtomicResource<T> : IAtomicResource<T> where T : class
{
    private readonly Func<T> _factory;
    private readonly Func<T, ValueTask> _teardown;

    // null, a T, or the terminal disposed sentinel. One atomic publication owns
    // both lifecycle and value, so creation/reset cannot resurrect a disposed owner.
    private object? _value;

    public AtomicResource(Func<T> factory, Func<T, ValueTask> teardown)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _teardown = teardown ?? throw new ArgumentNullException(nameof(teardown));
    }

    public bool IsDisposed => ReferenceEquals(Volatile.Read(ref _value), AtomicResourceSentinel.Disposed);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? GetOrCreate()
    {
        object? value = Volatile.Read(ref _value);
        if (value is null)
            return Create();

        return Unwrap(value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private T? Create()
    {
        T created = _factory();
        object? raced = Interlocked.CompareExchange(ref _value, created, null);
        if (raced is null)
            return created;

        // Cleanup consumes even source-backed ValueTasks. Disposal owns only the
        // value it detached; this losing candidate belongs to this creator.
        _ = Teardown(created);
        return Unwrap(raced);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? TryGet() => Unwrap(Volatile.Read(ref _value));

    public ValueTask Reset()
    {
        object? value = Volatile.Read(ref _value);
        if (ReferenceEquals(value, AtomicResourceSentinel.Disposed))
            return default;

        T fresh;
        try
        {
            fresh = _factory();
        }
        catch (Exception exception)
        {
            return ValueTask.FromException(exception);
        }

        while (true)
        {
            if (ReferenceEquals(value, AtomicResourceSentinel.Disposed))
                return Teardown(fresh);

            object? observed = Interlocked.CompareExchange(ref _value, fresh, value);
            if (ReferenceEquals(observed, value))
                return value is null ? default : Teardown((T)value);

            value = observed;
        }
    }

    public ValueTask DisposeAsync()
    {
        T? old = Detach();
        return old is null ? default : Teardown(old);
    }

    public void Dispose()
    {
        T? old = Detach();
        if (old is not null)
            Teardown(old).AwaitSync();
    }

    private T? Detach()
    {
        if (IsDisposed)
            return null;

        return Unwrap(Interlocked.Exchange(ref _value, AtomicResourceSentinel.Disposed));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T? Unwrap(object? value) => ReferenceEquals(value, AtomicResourceSentinel.Disposed) ? null : Unsafe.As<T>(value);

    private ValueTask Teardown(T value)
    {
        try
        {
            ValueTask pending = _teardown(value);
            if (!pending.IsCompletedSuccessfully)
                return AwaitTeardown(pending);

            pending.GetAwaiter().GetResult();
        }
        catch
        {
            // Cleanup is best effort, including synchronous callback failures.
        }

        return default;
    }

    private static async ValueTask AwaitTeardown(ValueTask pending)
    {
        try
        {
            await pending.ConfigureAwait(false);
        }
        catch
        {
            // Preserve best-effort cleanup for asynchronously failing callbacks.
        }
    }
}
