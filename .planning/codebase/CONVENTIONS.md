# Coding Conventions

**Analysis Date:** 2026-02-28

## Naming Patterns

**Files:**
- One class per file, file named after the class: `TripTimerApp.cs`, `AwtrixService.cs`
- Test files named after the class under test with `Tests` suffix: `TripTimerAppTests.cs`, `AwtrixServiceTests.cs`
- One file uses `Test` suffix (inconsistent): `AwtrixAppMessageTest.cs`
- Config classes grouped in `Configs/` subdirectory and suffixed with `Config`: `AppConfig.cs`, `TripTimerAppConfig.cs`
- Interface files in `Interfaces/` directory and prefixed with `I`: `IAwtrixApp.cs`, `IClock.cs`

**Classes:**
- PascalCase throughout: `TripTimerApp`, `AwtrixService`, `MqttConnector`
- App classes suffixed with `App`: `TripTimerApp`, `DiurnalApp`, `MqttRenderApp`
- Service classes suffixed with `Service`: `AwtrixService`, `TripPlannerService`, `TimerService`
- Controller classes suffixed with `Controller`: `TripTimerController`, `DiagnosticsController`
- Config classes suffixed with `Config`: `TripTimerAppConfig`, `AppConfig`, `MqttSettings`
- Event args suffixed with `EventArgs`: `ClockTickEventArgs`, `ButtonEventArgs`
- Internal constants class with `Names` suffix: `AppNames` (inside `Conductor.cs`)

**Methods:**
- PascalCase for public methods: `GetAlarmTime()`, `ActivateScheduledWork()`, `FormatClockString()`
- PascalCase for protected methods: `Initialize()`, `WakeUp()`, `ScheduleNextWakeUp()`
- camelCase for private methods: `ClockTickMinute()`, `ClockTickSecond()`, `LogAppConfigDetails()`

**Fields and Properties:**
- Private fields prefixed with underscore: `_httpPublisher`, `_tripPlanner`, `_cts`
- Public and protected properties use PascalCase: `AwtrixAddress`, `Config`, `Logger`, `Clock`
- Interface properties use PascalCase: `Now`, `AwtrixAddress`
- `internal` properties use PascalCase: `NextDepartures`, `IsScheduled`

**Variables:**
- camelCase for local variables: `baseTime`, `departureTime`, `alarmTimes`
- `sut` convention used in tests for the system under test

**Constants:**
- PascalCase for `const` fields: `ZeroFromMinutes` (inside method)
- PascalCase for `const string` members in constants classes: `AppNames.TripTimerApp`

**Namespaces:**
- Root namespace: `AwtrixSharpWeb`
- Subnamespaces mirror directory structure: `AwtrixSharpWeb.Apps.TripTimer`, `AwtrixSharpWeb.Services`
- Test namespaces mirror source: `Test.Apps`, `Test.Domain`, `Test.Services`
- TransportOpenData library uses its own root: `TransportOpenData`, `TransportOpenData.Tests`

## Code Style

**Formatting:**
- No `.editorconfig` or Roslyn formatter config detected — formatting is implicit
- 4-space indentation used consistently
- Allman-style braces (opening brace on new line) for classes and methods
- K&R style sometimes used for single-line switch case bodies
- Single blank line between methods; some inconsistency in blank lines within methods

**Nullable:**
- `<Nullable>enable</Nullable>` set in both `Test.csproj` and `TransportOpenData.Tests.csproj`
- Main API project (`awtrix-api.csproj`) should also have it — verify before adding nullable annotations
- Nullable reference types used for event handlers: `EventHandler<ClockTickEventArgs>?`
- `?` null-conditional operator used: `_stoppingCts?.Cancel()`, `_timer?.Dispose()`

**Implicit Usings:**
- `<ImplicitUsings>enable</ImplicitUsings>` in both test projects
- Test project has global using: `<Using Include="Xunit" />` — `[Fact]` and `[Theory]` available without import

## Import Organization

**Order (observed pattern):**
1. Project-internal namespaces first: `using AwtrixSharpWeb.Apps.TripTimer;`
2. Third-party library namespaces: `using Moq;`, `using NCrontab;`
3. System namespaces last: `using System;`, `using System.Collections.Generic;`

Note: Some files (especially older ones) have unordered imports with unused `System.*` namespaces left in, e.g., `using System.Xml.Linq;` in `TripTimerApp.cs`.

**Path Aliases:**
- None used — all imports are full namespace references

## Error Handling

**Patterns:**
- Exceptions in background tasks are caught and logged, not propagated: `catch (Exception ex) { Logger.LogError($"...", ex); }`
- `OperationCanceledException` always caught separately and treated as normal: does not log error
- Silent catch (bare `catch { }`) used as fallback for parse operations in `ValueMap.cs` and `AppConfig.cs`
- `throw new NotImplementedException(appConfig.Type)` used in `Conductor.AppFactory()` switch default — acts as a configuration guard
- `throw new NotSupportedException(...)` used in `AppConfig.ConvertValue()` for unsupported type conversions
- Controller methods wrap service calls in try/catch and return `Ok()`/`BadRequest()` or log + rethrow
- Async fire-and-forget with `_ = SomeAsync().Result` used in dispose paths (known pattern, not ideal)

**Logging levels:**
- `LogInformation`: Normal lifecycle events (starting, stopping, results)
- `LogDebug`: Frequent/verbose events (second/minute ticks, config parsing details)
- `LogWarning`: Non-fatal misconfiguration or unexpected states (empty config, unknown keys, missing device)
- `LogError`: Exceptions that break expected flow but allow continuation

## Logging

**Framework:** Microsoft.Extensions.Logging (`ILogger`, `ILogger<T>`, `ILoggerFactory`)

**Patterns:**
- Structured logging with message templates for controller/service code: `_logger.LogInformation("Creating {AppName} for {device}", ...)`
- Interpolated string logging for app code (inconsistent): `Logger.LogInformation($"Waking up for {Config.ActiveTime}")`
- Apps receive a non-generic `ILogger` (not `ILogger<T>`), injected via constructor from `ILoggerFactory.CreateLogger<T>()`
- Services use typed `ILogger<T>`: `ILogger<TimerService>`, `ILogger<HttpPublisher>`
- `Console.WriteLine` used sparingly in tests for diagnostic output

**Preferred pattern for new code:**
Use structured message templates (no interpolation) where possible: `Logger.LogInformation("App {App} woke up", Config.Name)`

## Comments

**When to Comment:**
- `<summary>` XML doc comments on public methods and classes that benefit from explanation
- Inline comments for non-obvious logic (regex fallback, quantization math)
- API endpoint docs reference external Awtrix docs: `/// <remarks>https://blueforcer.github.io/...</remarks>`
- Commented-out code left in place (e.g., entire `ValueMapsTests.cs` is commented out) — indicates work in progress

**JSDoc/TSDoc:**
- Not applicable (C# project)
- XML doc comments used on selected public members, not uniformly enforced

## Function Design

**Size:** Methods are generally small (under 30 lines). Larger methods like `ActivateScheduledWork()` (~45 lines) are acceptable when representing a full async workflow.

**Parameters:** Constructor injection preferred; long constructor parameter lists accepted for `Conductor` and `AwtrixApp<TConfig>` where all dependencies are explicit.

**Return Values:**
- `async Task<bool>` for publisher operations — returns success/failure
- `void` for event handlers (fire-and-forget side effects)
- Fluent builder pattern on `AwtrixAppMessage`: each `Set*()` method returns `this`
- Tuple returns for multi-value results: `(int quantized, int quantizedBlink)` from `Quantize()`

## Module Design

**Generics:**
- `AwtrixApp<TConfig>` and `ScheduledApp<TConfig>` use constrained generics: `where TConfig : AppConfig`
- `AppConfig.As<T>()` and `AppConfig.CreateFromAppConfig<T>()` use `where T : AppConfig, new()`

**Inheritance:**
- `AwtrixApp<TConfig>` → `ScheduledApp<TConfig>` → concrete app (e.g., `TripTimerApp`)
- `AwtrixApp<TConfig>` → concrete non-scheduled app (e.g., `DiurnalApp`, `SlackStatusApp`)
- `AwtrixPublisher` → `HttpPublisher` / `MqttPublisher`
- `AppConfig` → `ScheduledAppConfig` → `TripTimerAppConfig`

**Interfaces:**
- Thin interfaces for testability: `IAwtrixApp`, `IAwtrixService`, `IClock`, `ITimerService`, `ITripPlannerService`
- Interfaces live in `src/api/Interfaces/`

**Exports:**
- No barrel/index files — all types accessed by their namespace

---

*Convention analysis: 2026-02-28*
