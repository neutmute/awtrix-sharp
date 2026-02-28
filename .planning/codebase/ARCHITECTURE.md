# Architecture

**Analysis Date:** 2026-02-28

## Pattern Overview

**Overall:** Configuration-Driven Plugin Host with Event-Driven App Lifecycle

**Key Characteristics:**
- Apps are defined entirely in `appsettings.json` — no code changes needed to add app instances to devices
- `Conductor` (IHostedService) is the single orchestrator; it reads config and instantiates all apps at startup via a switch-based factory
- Apps communicate with Awtrix devices through a publisher abstraction (`AwtrixPublisher`) that transparently routes to MQTT or HTTP based on the `BaseTopic` prefix
- Cross-cutting event sources (MQTT messages, Slack events, timer ticks) are provided by singleton connector services and injected into apps that need them
- No DI container for app instances — `Conductor.AppFactory` creates apps with `new`, passing resolved singletons by hand

## Layers

**Configuration Layer:**
- Purpose: Bind `appsettings.json` into typed objects; provide runtime config to apps
- Location: `src/api/Domain/` and `src/api/Apps/Configs/`
- Contains: `AwtrixConfig`, `DeviceConfig`, `AppConfig`, `ScheduledAppConfig`, typed subconfigs (`TripTimerAppConfig`, `MqttAppConfig`, `SlackStatusAppConfig`)
- Depends on: Nothing
- Used by: `Conductor`, all `AwtrixApp<TConfig>` subclasses

**Hosted Services (Infrastructure) Layer:**
- Purpose: Manage long-running connections and time sources as ASP.NET `IHostedService`
- Location: `src/api/HostedServices/`
- Contains: `Conductor`, `MqttConnector`, `SlackConnector`, `TimerService`
- Depends on: Domain config, MQTTnet, SlackNet SDKs
- Used by: `Conductor` (injects all into app factory); Apps subscribe to events on `IMqttConnector` and `ITimerService`

**App Layer:**
- Purpose: Encapsulate per-device display logic and lifecycle
- Location: `src/api/Apps/`
- Contains: Abstract bases `AwtrixApp<TConfig>` and `ScheduledApp<TConfig>`, concrete apps (`TripTimerApp`, `DiurnalApp`, `MqttRenderApp`, `MqttClockRenderApp`, `SlackStatusApp`, `ButtonApp`)
- Depends on: Domain, Services (for `IAwtrixService`), Interfaces (for `IClock`, `IMqttConnector`, `ITimerService`)
- Used by: `Conductor` (holds `List<IAwtrixApp>`)

**Service Layer:**
- Purpose: Translate app-level device commands into transport-specific publishes
- Location: `src/api/Services/`
- Contains: `AwtrixService` (implements `IAwtrixService`), `AwtrixPublisher` (abstract), `HttpPublisher`, `MqttPublisher`, `TripPlannerService`
- Depends on: Domain messages, `MqttConnector`, `HttpClient`
- Used by: `AwtrixApp<TConfig>` base class methods (`Notify`, `AppUpdate`, `AppClear`, `Set`)

**API / Controller Layer:**
- Purpose: HTTP endpoints for diagnostics and manual app triggers via Swagger
- Location: `src/api/Controllers/`
- Contains: `DiagnosticsController`, `TripTimerController`, `MqttController`, `MqttRenderController`, `TripPlannerController`
- Depends on: `Conductor`, `AwtrixService`, `MqttConnector`, Domain config
- Used by: External operators / developers

**External Library Layer:**
- Purpose: Standalone client for Transport NSW Trip Planner REST API
- Location: `src/transportOpenData/`
- Contains: NSwag-generated `TripPlannerClient.nswag.cs`, `TransportOpenDataConfig`
- Depends on: Nothing (no ASP.NET dependency)
- Used by: `TripPlannerService` in the API project

## Data Flow

**App Publish Flow (MQTT transport):**
1. Cron timer fires inside `ScheduledApp.WaitForCronSchedule` → `WakeUp()` → `ActivateScheduledWork(cts)`
2. App builds an `AwtrixAppMessage` (fluent Dictionary builder)
3. App calls `AppUpdate(message)` on the `AwtrixApp` base
4. `AwtrixApp` delegates to `IAwtrixService.AppUpdate(awtrixAddress, appName, message)`
5. `AwtrixService.AppUpdate` calls `ResolvePublisher(topic)` — returns `MqttPublisher` when topic is not `http://`
6. `MqttPublisher.Publish(topic, json)` calls `MqttConnector.PublishAsync`
7. MQTTnet client sends payload to broker → broker routes to Awtrix clock device

**App Publish Flow (HTTP transport):**
- Same as above except `ResolvePublisher` returns `HttpPublisher` (topic starts with `http://`)
- `HttpPublisher.Publish(url, json)` sends HTTP POST to device IP

**MQTT Inbound Flow (MqttRenderApp):**
1. `MqttConnector.StartAsync` → establishes connection to broker
2. `MqttRenderApp.ActivateScheduledWork` → calls `_mqttConnector.Subscribe(Config.ReadTopic)`
3. Incoming MQTT message triggers `MqttConnector.MessageReceived` event
4. `MqttRenderApp.RawMessageReceived` filters by topic → `HandleMessage`
5. `HandleMessage` builds `AwtrixAppMessage`, applies matching `ValueMap` (regex decoration)
6. Calls `AppUpdate(message)` → follows App Publish Flow above

**Button Event Flow:**
1. `ButtonApp.Initialize` subscribes MQTT topics for Left/Select/Right buttons
2. Incoming `{baseTopic}/stats/button{Left|Select|Right}` message triggers `ButtonState.RegisterChange`
3. `ButtonState` fires `Click` or `DoubleClick` event (with double-click detection via `DoubleClickDetector`)
4. `Conductor` wires `buttonApp.DoubleClick` to `TripTimerApp.ExecuteNow()` for right-button press

**Slack Status Flow:**
1. `SlackConnector` maintains a Socket Mode WebSocket to Slack
2. On `UserChange` event, `SlackConnector.Handle` fires `UserStatusChanged`
3. `SlackStatusApp.UserStatusChanged` receives event, matches against `ValueMaps`, calls `AppUpdate` or `AppClear`

**State Management:**
- Apps hold their own state (e.g., `TripTimerApp.NextDepartures`, `MqttClockRenderApp._mqttValue`)
- No shared state store; each app instance is isolated per device
- `Conductor` holds `List<IAwtrixApp>` as the sole registry of live app instances

## Key Abstractions

**AwtrixApp<TConfig>:**
- Purpose: Abstract base for all display apps; owns the publish helpers and init lifecycle
- Examples: `src/api/Apps/AwtrixApp.cs`
- Pattern: Template Method — `Initialize()` is abstract; `Init()` calls `AppClear()` then `Initialize()`. Subclasses call `AppUpdate()`, `Notify()`, `AppClear()`, `Set()` which delegate to `IAwtrixService`.

**ScheduledApp<TConfig>:**
- Purpose: Extends `AwtrixApp` with NCrontab scheduling; runs `ActivateScheduledWork` on schedule then auto-reschedules
- Examples: `src/api/Apps/ScheduledApp.cs` — used by `TripTimerApp`, `MqttRenderApp`, `MqttClockRenderApp`, `DiurnalApp` (indirectly via `TimerService`)
- Pattern: Template Method — `ActivateScheduledWork(cts)` is abstract; base handles schedule/cancel/reschedule lifecycle. `CancellationTokenSource` on `_cts` is cancelled after `Config.ActiveTime` to end the active window.

**AwtrixAppMessage:**
- Purpose: Fluent builder for Awtrix display payloads; serializes to the device's JSON API format
- Examples: `src/api/Domain/AwtrixAppMessage.cs`
- Pattern: Extends `Dictionary<string,string>`; all `SetXxx` methods return `this` for chaining. `ToJson()` handles special case of embedded JSON arrays in `text` field.

**AwtrixPublisher / Transport Routing:**
- Purpose: Decouple apps from transport; `AwtrixService.ResolvePublisher` selects MQTT vs HTTP based on `BaseTopic` prefix
- Examples: `src/api/Services/AwtrixPublisher.cs`, `src/api/Services/HttpPublisher.cs`, `src/api/Services/MqttPublisher.cs`
- Pattern: Strategy — abstract `AwtrixPublisher` with concrete MQTT and HTTP implementations; selection is implicit from the topic string

**AppConfig / ValueMaps:**
- Purpose: Config is a flat `Dictionary<string,string>` (`AppConfigKeys`) plus a `List<ValueMap>`. Typed subconfigs (`ScheduledAppConfig`, `TripTimerAppConfig`) project strongly-typed properties over the dictionary via `GetConfig<T>` / `SetConfig`.
- Examples: `src/api/Apps/Configs/AppConfig.cs`, `src/api/Apps/Configs/ValueMap.cs`
- Pattern: `AppConfig.As<T>()` performs a "widening cast" — copies base config into a typed subclass. `ValueMap.Decorate(message, logger)` applies matched overrides to an `AwtrixAppMessage` via switch + reflection fallback.

## Entry Points

**Program.cs:**
- Location: `src/api/Program.cs`
- Triggers: `dotnet run` / `dotnet awtrix-api.dll`
- Responsibilities: Builds `WebApplication`; registers all services (singletons, transients, `IHostedService` registrations); configures middleware (Swagger, HTTPS redirect, controllers)

**Conductor (IHostedService):**
- Location: `src/api/HostedServices/Conductor.cs`
- Triggers: ASP.NET host `StartAsync` — runs after `MqttConnector` and `SlackConnector` have started
- Responsibilities: Iterates `AwtrixConfig.Devices`; calls `AppFactory` for each configured app entry; wires button events; calls `app.Init()` on all created apps

**REST Controllers:**
- Location: `src/api/Controllers/`
- Triggers: HTTP requests (Swagger UI at `/swagger`, or direct POST/GET)
- Responsibilities: `DiagnosticsController` — test notifications and settings; `TripTimerController` — trigger `TripTimerApp` immediately or test alarm timings; `MqttController` — publish raw MQTT; `TripPlannerController` — invoke Transport NSW API directly; `MqttRenderController` — render values manually

## Error Handling

**Strategy:** Log-and-continue; errors in individual apps do not crash the host process.

**Patterns:**
- `ScheduledApp.WakeUp` wraps `ActivateScheduledWork` in try/catch/finally — always reschedules via `ScheduleNextWakeUp()` even after error
- `MqttConnector.PublishAsync` catches publish exceptions and calls `ConnectAsync()` to self-heal
- `SlackConnector` has retry loop with 5-second delay on WebSocket errors
- `ValueMap.Decorate` and `ApplyDynamicProperty` catch all exceptions and log warnings — never throws
- `AppConfig.ConvertValue` throws `NotSupportedException` for unknown types (intended — indicates programmer error)

## Cross-Cutting Concerns

**Logging:** `ILogger<T>` injected everywhere. Console output via `AddSimpleConsole` with `HH:mm:ss` timestamp prefix. Log level `Information` default; `Microsoft.AspNetCore` at `Warning`.

**Validation:** No explicit input validation layer. Configuration validation is implicit — missing required `AppConfig` keys throw at first use (`ConvertValue` with parse errors).

**Authentication:** No HTTP auth on REST endpoints. Slack auth via `AWTRIXSHARP_SLACK__APPTOKEN` env var read at runtime in `SlackConnector.ExecuteAsync`. MQTT auth via `MqttSettings.Username`/`Password` from config.

**Time abstraction:** `IClock` interface (`src/api/Interfaces/IClock.cs`) wraps `DateTimeOffset.Now`; concrete `Clock` class in `src/api/Domain/Clock.cs`. Used by `ScheduledApp` and `TripTimerApp` for testable time.

---

*Architecture analysis: 2026-02-28*
