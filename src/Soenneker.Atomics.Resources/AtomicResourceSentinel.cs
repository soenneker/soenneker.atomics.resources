namespace Soenneker.Atomics.Resources;

internal static class AtomicResourceSentinel
{
    internal static readonly object Disposed = new();
}
