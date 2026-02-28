# Codebase Structure

**Analysis Date:** 2026-02-28

## Directory Layout

```
awtrix-sharp/                          # Repository root
├── src/
│   ├── api/                           # ASP.NET Core host — main deployable project
│   │   ├── Apps/                      # All Awtrix app implementations
│   │   │   ├── Buttons/               # ButtonApp + click/double-click detection
│   │   │   ├── Configs/               # AppConfig base + ValueMap + ScheduledAppConfig
│   │   │   ├── Diurnal/               # DiurnalApp (time-of-day brightness/color)
│   │   │   ├── MqttRender/            # MqttRenderApp + MqttClockRenderApp + MqttAppConfig
│   │   │   ├── SlackStatus/           # SlackStatusApp + SlackStatusAppConfig
│   │   │   ├── TripTimer/             # TripTimerApp + TripTimerAppConfig
│   │   │   ├── AwtrixApp.cs           # Abstract base for all apps
│   │   │   └── ScheduledApp.cs        # Abstract base for cron-scheduled apps
│   │   ├── Controllers/               # REST API controllers (Swagger-documented)
│   │   ├── Debug/                     # SlackUserHarvester (dev utility only)
│   │   ├── Domain/                    # Core domain types and config models
│   │   ├── HostedServices/            # IHostedService implementations
│   │   │   ├── Slack/                 # Slack event arg types
│   │   │   ├── Conductor.cs           # App orchestrator (factory + lifecycle)
│   │   │   ├── MqttConnector.cs       # MQTT broker connection
│   │   │   ├── SlackConnector.cs      # Slack Socket Mode listener
│   │   │   └── TimerService.cs        # Second/minute tick events
│   │   ├── Interfaces/                # Service interfaces for testability
│   │   ├── Properties/                # launchSettings.json
│   │   ├── Services/                  # Service implementations
│   │   │   └── TripPlanner/           # TripPlannerService + TripSummary + TimePlace
│   │   ├── appsettings.json           # Main config — defines devices and app instances
│   │   ├── appsettings.Development.json
│   │   └── Program.cs                 # Composition root — DI setup and middleware pipeline
│   └── transportOpenData/             # Standalone Transport NSW API client library
│       └── TripPlanner/               # NSwag-generated TripPlannerClient + config
├── test/
│   ├── Test/                          # Main unit test project (32 tests)
│   │   ├── Apps/                      # App tests (MockClock, TripTimerAppTests)
│   │   ├── Configs/                   # AppConfig + ValueMaps tests
│   │   ├── Domain/                    # AwtrixAppMessage + TripSummary + TripTimerAppConfig tests
│   │   └── Services/                  # AwtrixService + TimerService tests
│   └── transportOpenData.Tests/       # Transport NSW client tests (11 tests)
│       ├── Helpers/                   # TestDataHelper for loading JSON fixtures
│       ├── TestData/                  # Real JSON fixtures from Transport NSW API
│       └── TripPlanner/               # Deserialization and attribute tests
├── docs/
│   └── gifs/                          # Demo GIFs for readme
├── .github/
│   └── workflows/                     # docker-publish.yml CI/CD
├── .planning/
│   └── codebase/                      # GSD codebase analysis documents
├── awtrix-sharp.sln                   # Visual Studio solution file
├── Dockerfile                         # Multi-stage Docker build
├── build.ps1                          # docker build wrapper
├── CLAUDE.md                          # Project guidance for Claude Code
└── readme.md                          # Project readme with examples
```

## Directory Purposes

**`src/api/Apps/`:**
- Purpose: All pluggable app implementations that push content to Awtrix devices
- Contains: Abstract base classes, 6 concrete app types, their config classes
- Key files: `AwtrixApp.cs`, `ScheduledApp.cs`, `Configs/AppConfig.cs`, `Configs/ValueMap.cs`

**`src/api/Apps/Configs/`:**
- Purpose: Config system — base `AppConfig` (flat string dictionary + ValueMaps), typed subconfig classes
- Contains: `AppConfig.cs`, `AppConfigKeys.cs`, `ScheduledAppConfig.cs`, `ValueMap.cs`
- Key files: `AppConfig.cs` — the central config mechanism with `As<T>()` widening cast and `FindMatchingValueMap`

**`src/api/Domain/`:**
- Purpose: Core value types with no dependencies on other project layers
- Contains: `AwtrixAppMessage.cs`, `AwtrixConfig.cs`, `AwtrixAddress.cs`, `AwtrixSettings.cs`, `DeviceConfig.cs`, `MqttSettings.cs`, `Clock.cs`
- Key files: `AwtrixAppMessage.cs` — fluent builder used by every app to construct display payloads

**`src/api/HostedServices/`:**
- Purpose: Long-running ASP.NET background services that maintain external connections
- Contains: `Conductor.cs` (app factory/lifecycle), `MqttConnector.cs`, `SlackConnector.cs`, `TimerService.cs`
- Key files: `Conductor.cs` — the `AppFactory` switch statement is where new app types must be registered

**`src/api/Interfaces/`:**
- Purpose: Interfaces used for dependency injection and testability
- Contains: `IAwtrixApp.cs`, `IAppConfig.cs`, `IClock.cs`, `IMqttConnector.cs`, `ITimerService.cs`, `ITripPlannerService.cs`

**`src/api/Services/`:**
- Purpose: Implement transport-level operations and business services
- Contains: `AwtrixService.cs`, `AwtrixPublisher.cs` (abstract), `HttpPublisher.cs`, `MqttPublisher.cs`, `TripPlannerService.cs`
- Key files: `AwtrixService.cs` — routes publish calls to MQTT or HTTP based on `BaseTopic` prefix

**`src/api/Controllers/`:**
- Purpose: REST API surface for manual triggers and diagnostics
- Contains: `DiagnosticsController.cs`, `TripTimerController.cs`, `MqttController.cs`, `MqttRenderController.cs`, `TripPlannerController.cs`

**`src/transportOpenData/`:**
- Purpose: Standalone library with no ASP.NET dependency; wraps Transport NSW Trip Planner API
- Contains: `TransportOpenDataConfig.cs`, `TripPlanner/TripPlannerClient.nswag.cs` (NSwag-generated)

**`test/Test/`:**
- Purpose: Unit tests for the `src/api` project
- Contains: Test classes mirroring the `src/api` namespace structure

**`test/transportOpenData.Tests/`:**
- Purpose: Deserialization and integration tests for the Transport NSW client
- Contains: `TestData/` directory with real JSON API response fixtures

## Key File Locations

**Entry Points:**
- `src/api/Program.cs`: Composition root — all DI registrations and middleware pipeline
- `src/api/HostedServices/Conductor.cs`: App factory and lifecycle orchestrator

**Configuration:**
- `src/api/appsettings.json`: Device definitions and app instance configurations
- `src/api/Apps/Configs/AppConfig.cs`: Base config class used by all apps
- `src/api/Apps/Configs/ValueMap.cs`: Regex-based display override system

**Core Abstractions:**
- `src/api/Apps/AwtrixApp.cs`: Abstract base — all apps inherit from this
- `src/api/Apps/ScheduledApp.cs`: Abstract base for cron-scheduled apps
- `src/api/Domain/AwtrixAppMessage.cs`: Fluent builder for device display messages
- `src/api/Services/AwtrixService.cs`: Device command dispatcher (MQTT vs HTTP routing)
- `src/api/Services/AwtrixPublisher.cs`: Abstract transport base class

**Infrastructure:**
- `src/api/HostedServices/MqttConnector.cs`: MQTT broker connection (MQTTnet 5)
- `src/api/HostedServices/SlackConnector.cs`: Slack Socket Mode connection
- `src/api/HostedServices/TimerService.cs`: Second/minute tick event source

**App Implementations:**
- `src/api/Apps/TripTimer/TripTimerApp.cs`: Train departure countdown with Transport NSW API
- `src/api/Apps/Diurnal/DiurnalApp.cs`: Time-of-day brightness and color changes
- `src/api/Apps/MqttRender/MqttRenderApp.cs`: Renders subscribed MQTT topic value
- `src/api/Apps/MqttRender/MqttClockRenderApp.cs`: Clock + MQTT value combined display
- `src/api/Apps/SlackStatus/SlackStatusApp.cs`: Mirrors Slack presence/status
- `src/api/Apps/Buttons/ButtonApp.cs`: Hardware button press detection

**Testing:**
- `test/Test/`: All unit tests for `src/api`
- `test/transportOpenData.Tests/TestData/`: JSON fixtures for Transport NSW API deserialization tests

## Naming Conventions

**Files:**
- Classes: PascalCase matching class name, one class per file — e.g., `TripTimerApp.cs`, `AwtrixAppMessage.cs`
- Interfaces: `I` prefix + PascalCase — e.g., `IAwtrixApp.cs`, `IClock.cs`
- Config classes: Suffix `Config` or `AppConfig` — e.g., `TripTimerAppConfig.cs`, `MqttAppConfig.cs`
- App config properties: Suffix `Settings` for device-level settings — e.g., `AwtrixSettings.cs`, `MqttSettings.cs`
- Test files: Suffix `Tests` — e.g., `TripTimerAppTests.cs`, `AwtrixServiceTests.cs`

**Directories:**
- App subdirectories named after their app type: `TripTimer/`, `MqttRender/`, `SlackStatus/`, `Diurnal/`, `Buttons/`
- Test subdirectories mirror source namespaces: `test/Test/Apps/`, `test/Test/Services/`

**Namespaces:**
- API project root: `AwtrixSharpWeb`
- Transport library: `TransportOpenData`
- Sub-namespaces follow directory: `AwtrixSharpWeb.Apps.TripTimer`, `AwtrixSharpWeb.HostedServices`, `AwtrixSharpWeb.Services`

## Where to Add New Code

**New App Type:**
1. Create directory: `src/api/Apps/{AppName}/`
2. Add config class: `src/api/Apps/{AppName}/{AppName}Config.cs` — extend `AppConfig` or `ScheduledAppConfig`
3. Add app class: `src/api/Apps/{AppName}/{AppName}App.cs` — extend `AwtrixApp<TConfig>` or `ScheduledApp<TConfig>`
4. Register in factory: Add `case AppNames.{AppName}:` in `Conductor.AppFactory` switch — `src/api/HostedServices/Conductor.cs` lines 147–198
5. Add constant: Add `public const string {AppName}App = "{AppName}App";` to `AppNames` class at top of `Conductor.cs`
6. Add to `appsettings.json`: New entry under a device's `Apps[]` array with `"Type": "{AppName}App"` and `"Config": {...}`
7. Add tests: `test/Test/Apps/{AppName}Tests.cs`

**New Controller:**
- Add to: `src/api/Controllers/{Name}Controller.cs`
- Inject `Conductor` if the controller needs to interact with running apps

**New Domain Type:**
- Add to: `src/api/Domain/` if it is a shared value object; add to `src/api/Apps/{AppName}/` if it is app-specific

**New Service:**
- Add interface to: `src/api/Interfaces/I{ServiceName}.cs`
- Add implementation to: `src/api/Services/{ServiceName}.cs`
- Register in: `src/api/Program.cs` `services.Add...` block

**New ValueMap Property:**
- To add a new display override key recognized by `ValueMap.Decorate`, add a case to the switch in `src/api/Apps/Configs/ValueMap.cs` line 77

## Special Directories

**`src/transportOpenData/TripPlanner/`:**
- Purpose: NSwag-generated strongly-typed HTTP client for Transport NSW API
- Generated: Yes — `TripPlannerClient.nswag.cs` is auto-generated from the OpenAPI spec
- Committed: Yes — generated file is committed to source control

**`test/transportOpenData.Tests/TestData/`:**
- Purpose: Real JSON responses from the Transport NSW API used as test fixtures
- Generated: No — captured manually
- Committed: Yes

**`src/api/Debug/`:**
- Purpose: Development-only utilities (e.g., `SlackUserHarvester` for discovering Slack user IDs)
- Generated: No
- Committed: Yes — not excluded, but not wired into production DI

**`.planning/codebase/`:**
- Purpose: GSD codebase analysis documents consumed by planning and execution commands
- Generated: Yes — by `gsd:map-codebase`
- Committed: No (in `.gitignore` under `.claude/`)

---

*Structure analysis: 2026-02-28*
