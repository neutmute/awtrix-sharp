# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Project Does

AwtrixSharp is a .NET 9 ASP.NET Core background service that controls [Awtrix 3](https://blueforcer.github.io/awtrix3) smart clock devices. It supports multiple clocks simultaneously via MQTT or HTTP, with pluggable "apps" that push display content on a schedule.

## Build & Test Commands

```bash
# Build
dotnet build

# Run all tests
dotnet test

# Run tests for a specific project
dotnet test test/Test/Test.csproj
dotnet test test/transportOpenData.Tests/TransportOpenData.Tests.csproj

# Run a single test
dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~ClassName.MethodName"

# Run the application locally
dotnet run --project src/api

# Build Docker image
docker build -t awtrix-sharp .
# or: ./build.ps1
```

## Architecture

### Core Pattern: Configuration-Driven Plugin Apps

Apps are created at startup by `Conductor` (a hosted service) by reading `appsettings.json`. Each device lists app entries with a `Type` string (class name) and a `Config` block. There is no reflection magic — app instantiation happens in the factory inside `Conductor`.

```
appsettings.json → Conductor → App Factory → AwtrixApp instances
                                                   ↓
                                            TimerService (cron)
                                                   ↓
                                            AwtrixService → Publishers (MQTT / HTTP)
                                                                   ↓
                                                            Awtrix Device
```

### Key Classes

| Class | Location | Role |
|---|---|---|
| `Conductor` | `src/api/HostedServices/Conductor.cs` | Orchestrates app lifecycle; IHostedService entry point |
| `AwtrixApp<TConfig>` | `src/api/Apps/AwtrixApp.cs` | Abstract base for all apps; holds publish helpers |
| `ScheduledApp` | `src/api/Apps/ScheduledApp.cs` | Base for cron-scheduled apps (uses NCrontab) |
| `AwtrixService` | `src/api/Services/AwtrixService.cs` | Sends Notify/Dismiss/Update/Clear to a device |
| `AwtrixAppMessage` | `src/api/Domain/AwtrixAppMessage.cs` | Fluent builder for display messages (extends `Dictionary<string,string>`) |
| `MqttConnector` | `src/api/HostedServices/MqttConnector.cs` | Maintains MQTT broker connection (MQTTnet 5) |
| `SlackConnector` | `src/api/HostedServices/SlackConnector.cs` | Slack Socket Mode listener |

### Built-in Apps

- **`TripTimerApp`** — Countdown to train departure via Transport NSW API
- **`DiurnalApp`** — Scheduled brightness/color changes (day vs night)
- **`MqttRenderApp`** — Subscribe to MQTT topic, render value on clock
- **`MqttClockRenderApp`** — Clock + MQTT value (e.g. temperature)
- **`SlackStatusApp`** — Shows Slack presence/status

### ValueMaps

Every app config can include a `ValueMaps[]` array. Each entry has a `ValueMatcher` (regex) plus display overrides (`Icon`, `Color`, `Text`, etc.). When an app produces a value, it is tested against matchers in order, and the first match wins. This is the primary extension point for customising display without writing new apps.

### Two Publisher Transports

- **MQTT** (`MqttPublisher`) — sends to `{BaseTopic}/custom/{appName}`
- **HTTP** (`HttpPublisher`) — REST calls to the device's local IP

`AwtrixAddress` on each device config selects which transport to use.

### TransportOpenData Library

`src/transportOpenData/` is a standalone class library (no ASP.NET dependency) wrapping the Transport NSW Trip Planner REST API. The test project has real JSON fixtures in `test/transportOpenData.Tests/TestData/` used for deserialization tests.

## Configuration

Local secrets go in .NET User Secrets (project has a `UserSecretsId`). In production, use environment variables with the `AWTRIXSHARP_` prefix and double-underscore for nesting:

```
AWTRIXSHARP_MQTT__HOST
AWTRIXSHARP_MQTT__USERNAME
AWTRIXSHARP_MQTT__PASSWORD
AWTRIXSHARP_SLACK__USERID
AWTRIXSHARP_SLACK__APPTOKEN
TRANSPORTOPENDATA__APIKEY
```

## CI/CD

`.github/workflows/docker-publish.yml` builds and pushes `ghcr.io/neutmute/awtrix-sharp` on every push to `master` and on `v*.*.*` tags. PRs trigger a build-only run (no push, no cosign signing).
