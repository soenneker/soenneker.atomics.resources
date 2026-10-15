using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Soenneker.Atomics.Resources.Abstract;
using Soenneker.Atomics.ValueResources;

namespace Soenneker.Atomics.Resources;

public sealed class AtomicResource<T> : IAtomicResource<T> where T : class
{
    private readonly Func<T> _factory;
    private readonly Func<T, ValueTask> _teardown;

    private ValueAtomicResource<T> _resource;

    public AtomicResource(Func<T> factory, Func<T, ValueTask> teardown)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _teardown = teardown ?? throw new ArgumentNullException(nameof(teardown));
    }

    public bool IsDisposed => _resource.IsDisposed;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? GetOrCreate() => _resource.GetOrCreate(_factory, _teardown);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? TryGet() => _resource.TryGet();

    public ValueTask Reset() => _resource.Reset(_factory, _teardown);

    public ValueTask DisposeAsync() => _resource.DisposeAsync(_teardown);

    public void Dispose() => _resource.Dispose(_teardown);
}
