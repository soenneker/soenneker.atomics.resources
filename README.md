[![](https://img.shields.io/nuget/v/soenneker.atomics.resources.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.atomics.resources/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.atomics.resources/publish-package.yml?style=for-the-badge)](https://github.com/soenneker/soenneker.atomics.resources/actions/workflows/publish-package.yml)
[![](https://img.shields.io/nuget/dt/soenneker.atomics.resources.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.atomics.resources/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.atomics.resources/codeql.yml?label=CodeQL&style=for-the-badge)](https://github.com/soenneker/soenneker.atomics.resources/actions/workflows/codeql.yml)

# Soenneker.Atomics.Resources

Provides thread-safe lazy creation, replacement, and disposal of a reference-type resource.

## Install

```bash
dotnet add package Soenneker.Atomics.Resources
```

## Quick start

```csharp
using Soenneker.Atomics.Resources.Abstract;

IAtomicResource<T> atomicResource = /* resolve from DI */;
var result = atomicResource.GetOrCreate();
```

Gets the current instance, creating it if necessary.

## What you get

- `IAtomicResource<T>` — Provides thread-safe lazy creation, replacement, and disposal of a reference-type resource.

## API at a glance

| API | What it does | Result / important behavior |
| --- | --- | --- |
| `IAtomicResource<T>.GetOrCreate()` | Gets the current instance, creating it if necessary. | The current instance, or `null` if disposed. |
| `IAtomicResource<T>.TryGet()` | Returns the current instance if present, without creating a new one. | The existing instance, or `null` if none has been created or the resource is disposed. |
| `IAtomicResource<T>.Reset()` | Atomically replaces the current instance with a freshly created one, and asynchronously tears down the previous instance (if any). | A task that completes when the reset operation is complete. |
| `IAtomicResource<T>.IsDisposed` | Indicates whether the resource has been disposed and will no longer create or return instances. | Indicates whether the resource has been disposed and will no longer create or return instances. |

## Important behavior

- `IAtomicResource<T>.GetOrCreate()`: Implementations should be safe for concurrent callers and avoid duplicate allocations (i.e., publish-at-most-once semantics per reset). If the resource has been disposed, this should return `null`.
- `IAtomicResource<T>.Reset()`: After this completes, subsequent `GetOrCreate` calls return the replacement instance.

## Practical notes

- Dispose instances you own when their scope ends so held resources can be released.
