# Nordstein.Core.Common

Product-agnostic building blocks shared across Nordstein applications.

| Area | What it gives you |
|------|-------------------|
| `Time` | `IClock` / `SystemClock` — the seam that makes time-dependent logic testable |
| `Random` | `IRandom` / `SeededRandom` — the same seam for randomness |
| `Async` | `IAsyncLock` / `AsyncLock`, `TaskExtensions` |
| `Validation` | `Validation` helpers, `ValidatorExtensions`, string guards |
| `Conversion` | `ITypeConverter` and the conversion extensions |
| `DependencyInjection` | Autofac registration helpers, including `RegisterServiceCollection` |
| `Hosting` | `AddResilientBackgroundServices`, `IAppVersion`, `NullHostedService` |
| `Lifecycle` | `Disposable`, `ITempDirectory` |
| `Net` | Endpoint URL parsing and validation |
| `Security` | `Sha256` |
| `Serialization` | `ISerializer` and a `System.Text.Json` implementation |
| `Text` | `LogSafeExtensions`, `SlugExtensions` |

## Registration

Everything is wired through one Autofac module:

```csharp
builder.RegisterModule<Nordstein.Core.Common.Module>();
```

## Notes

`AddResilientBackgroundServices` sets `HostOptions.BackgroundServiceExceptionBehavior` to
`Ignore`. .NET's default is `StopHost`, which means one throwing `BackgroundService` stops the
whole host and exits with code 0 — a clean shutdown that no restart policy treats as a failure.
Call it from every host that runs long-running loops.
