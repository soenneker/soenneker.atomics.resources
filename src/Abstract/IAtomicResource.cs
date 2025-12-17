using System;
using System.Threading.Tasks;

namespace Soenneker.Atomics.Resources.Abstract;

/// <summary>
/// Thread-safe holder for a single resource that can be lazily created,
/// atomically reset (swap), and asynchronously torn down.
/// </summary>
/// <typeparam name="T">The resource type being managed.</typeparam>
public interface IAtomicResource<out T> : IAsyncDisposable, IDisposable where T : class
{
    /// <summary>
    /// Gets the current instance, creating it if necessary.
    /// </summary>
    /// <remarks>
    /// Implementations should be safe for concurrent callers and avoid
    /// duplicate allocations (i.e., publish-at-most-once semantics per reset).
    /// If the resource has been disposed, this should return <c>null</c>.
    /// </remarks>
    /// <returns>The current instance, or <c>null</c> if disposed.</returns>
    T? GetOrCreate();

    /// <summary>
    /// Returns the current instance if present, without creating a new one.
    /// </summary>
    /// <returns>The existing instance, or <c>null</c> if none has been created or the resource is disposed.</returns>
    T? TryGet();

    /// <summary>
    /// Atomically replaces the current instance with a freshly created one,
    /// and asynchronously tears down the previous instance (if any).
    /// </summary>
    /// <remarks>
    /// After this completes, subsequent <see cref="GetOrCreate"/> calls should return the new instance.
    /// </remarks>
    ValueTask Reset();

    /// <summary>
    /// Indicates whether the resource has been disposed and will no longer create or return instances.
    /// </summary>
    bool IsDisposed { get; }
}