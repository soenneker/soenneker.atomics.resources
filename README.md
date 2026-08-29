[![](https://img.shields.io/nuget/v/soenneker.atomics.resources.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.atomics.resources/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.atomics.resources/publish-package.yml?style=for-the-badge)](https://github.com/soenneker/soenneker.atomics.resources/actions/workflows/publish-package.yml)
[![](https://img.shields.io/nuget/dt/soenneker.atomics.resources.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.atomics.resources/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.atomics.resources/codeql.yml?label=CodeQL&style=for-the-badge)](https://github.com/soenneker/soenneker.atomics.resources/actions/workflows/codeql.yml)

# Soenneker.Atomics.Resources

Lazy, atomic publication and replacement of a reference-type resource with application-defined teardown.

`AtomicResource<T>` is useful for shared resources that can be recreated, such as a client or compiled snapshot. It publishes one winning reference and cleans up replaced or losing instances through the supplied callback.

## Installation

```bash
dotnet add package Soenneker.Atomics.Resources
```

## Create a resource holder

This package has no DI registrar. Construct the holder with a synchronous factory and a teardown callback, then register or own that holder as appropriate:

```csharp
using Soenneker.Atomics.Resources;

var resource = new AtomicResource<HttpClient>(
    factory: () => new HttpClient
    {
        BaseAddress = new Uri("https://api.example.com")
    },
    teardown: client =>
    {
        client.Dispose();
        return ValueTask.CompletedTask;
    });

HttpClient? client = resource.GetOrCreate();
```

`GetOrCreate` returns `null` after disposal. Before disposal, concurrent callers may each invoke the factory, but only one created instance is published. Instances that lose the publication race are passed to teardown without awaiting its `ValueTask`.

Factories should therefore be safe to run concurrently, and teardown for race losers should complete synchronously or safely continue without an observer.

## Read and replace

`TryGet` reads the current reference without creating it:

```csharp
HttpClient? existing = resource.TryGet();
```

`Reset` creates and publishes a replacement, then awaits teardown of the previous instance:

```csharp
await resource.Reset();
```

The replacement is visible before old-resource teardown completes. Existing callers may still hold and use the old reference while teardown is running; `AtomicResource<T>` does not provide leases or usage tracking. Use higher-level coordination when a resource cannot be torn down until all borrowers finish.

Teardown exceptions from `Reset`, `Dispose`, and `DisposeAsync` are intentionally swallowed.

## Disposal

```csharp
await resource.DisposeAsync();
```

Disposal marks the holder closed, removes the currently published instance, and invokes teardown. `Dispose` blocks on the teardown `ValueTask`; `DisposeAsync` awaits it. Both are safe to call repeatedly.

Do not call `Reset` concurrently with disposal. `Reset` checks disposal before creating the replacement, so a disposal race after that check can publish a new instance during shutdown. Coordinate lifecycle operations in the owning service.

## Operational constraints

- `T` must be a reference type.
- Creation is synchronous; use another abstraction when initialization itself must be awaited.
- The holder makes reference publication atomic, not operations performed by `T`.
- Avoid concurrent `Reset` calls unless the factory and teardown tolerate multiple replacements and overlapping cleanup.
- A returned reference is not protected from a later reset or disposal.

## API

| Member | Behavior |
| --- | --- |
| `GetOrCreate()` | Returns the published instance, creates a candidate, or returns `null` after disposal. |
| `TryGet()` | Reads the current instance without creating it. |
| `Reset()` | Publishes a fresh instance and tears down the previous one. |
| `IsDisposed` | Indicates that the holder has been closed. |
| `Dispose()` / `DisposeAsync()` | Removes and tears down the current instance. |
