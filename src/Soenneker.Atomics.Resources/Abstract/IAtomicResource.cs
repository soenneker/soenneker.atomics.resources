using System;
using System.Threading.Tasks;

namespace Soenneker.Atomics.Resources.Abstract;

/// <summary>
/// Provides thread-safe lazy creation, replacement, and disposal of a reference-type resource.
/// </summary>
/// <remarks>
/// Factories must return fresh, non-null instances. Teardown is best effort: synchronous and asynchronous
/// callback failures are suppressed. Returned references are not leases; concurrent reset or disposal
/// may tear them down while they are in use. Disposal is terminal and waits only for the cleanup it owns,
/// not other operations' in-flight factories or cleanup. Repeated disposal is a no-op.
/// Prefer asynchronous disposal when teardown may capture the caller's synchronization context.
/// </remarks>
public interface IAtomicResource<out T> : IAsyncDisposable, IDisposable where T : class
{
    /// <summary>
    /// Gets the current instance, creating it if necessary.
    /// </summary>
    /// <remarks>
    /// Concurrent callers share the published resource. Factories may run concurrently;
    /// candidates that lose publication are torn down.
    /// Losing candidates are cleaned up asynchronously without blocking this call.
    /// Factory exceptions propagate and allow a later retry; a null result throws <see cref="InvalidOperationException"/>.
    /// If the resource has been disposed, this returns <c>null</c>.
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
    /// <returns>A task that completes when the reset operation is complete.</returns>
    /// <remarks>
    /// The replacement is published before teardown begins. A concurrent reset or disposal may supersede it.
    /// Factory failures, including null results, leave the current instance unchanged and fault the returned task.
    /// Does nothing after disposal; if disposal wins during creation, the new candidate is torn down instead.
    /// </remarks>
    ValueTask Reset();

    /// <summary>
    /// Indicates whether the resource has been disposed and will no longer create or return instances.
    /// </summary>
    bool IsDisposed { get; }
}
