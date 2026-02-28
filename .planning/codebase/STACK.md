# Technology Stack

**Analysis Date:** 2026-02-28

## Languages

**Primary:**
- C# 13 (implicit, .NET 9 SDK) - All application and library code
- JSON - Configuration (`appsettings.json`), test fixtures (`test/transportOpenData.Tests/TestData/`)

**Secondary:**
- PowerShell - Build script (`build.ps1`)
- YAML - CI/CD workflow (`.github/workflows/docker-publish.yml`)
- Dockerfile - Container build definition (`Dockerfile`)

## Runtime

**Environment:**
- .NET 9 (ASP.NET Core) — target framework `net9.0` across all projects
  - Note: Memory indicates the machine has .NET SDK 10.0.103 and some bin artifacts reflect `net10.0`; csproj files still declare `net9.0`

**Package Manager:**
- NuGet (managed via SDK-style `.csproj` `<PackageReference>` elements)
- Lockfile: Not present (no `packages.lock.json` committed)

## Frameworks

**Core:**
- ASP.NET Core 9 (`Microsoft.NET.Sdk.Web`) — hosts the background service, REST controllers, and Swagger UI
- `Microsoft.Extensions.Hosting` — `IHostedService` pattern for long-running services

**Testing:**
- xUnit 2.9.2 (`test/Test/Test.csproj`) — primary test runner for API project tests
- xUnit 2.7.0 (`test/transportOpenData.Tests/TransportOpenData.Tests.csproj`) — test runner for transport library tests
- Moq 4.20.72 — mocking framework (API test project only)
- coverlet.collector 6.0.2 — code coverage collection

**Build/Dev:**
- Swashbuckle.AspNetCore 9.0.3 + Swashbuckle.AspNetCore.Annotations 9.0.4 — OpenAPI/Swagger UI
- NSwag 14.5.0 (codegen tool, not runtime dep) — generated `src/transportOpenData/TripPlanner/TripPlannerClient.nswag.cs`
- Microsoft.VisualStudio.Azure.Containers.Tools.Targets 1.22.1 — VS Docker tooling

## Key Dependencies

**Critical:**
- `MQTTnet` 5.0.1.1416 — MQTT v5 client; connects to broker and sends/receives messages to Awtrix devices and home-automation topics (`src/api/HostedServices/MqttConnector.cs`)
- `NCrontab` 3.3.3 — Cron expression parsing for `ScheduledApp` activation windows (`src/api/Apps/ScheduledApp.cs`)
- `SlackNet` 0.17.4 — Slack Socket Mode client for receiving user-change events (`src/api/HostedServices/SlackConnector.cs`)
- `Newtonsoft.Json` 13.0.3 — JSON deserialization in transport test project (`test/transportOpenData.Tests/`)

**Infrastructure:**
- `Microsoft.Extensions.Logging` 9.0.8 — structured console + debug logging across all projects
- `Microsoft.Extensions.Logging.Abstractions` 9.0.8 — used by standalone `TransportOpenData` library
- `Microsoft.AspNetCore.OpenApi` 9.0.8 — OpenAPI metadata (present in csproj; Swashbuckle is also registered)
- `Microsoft.NET.Test.Sdk` 17.12.0 — test host for MSTest/xUnit

## Configuration

**Environment:**
- `appsettings.json` loaded at startup (`src/api/Program.cs`: `AddJsonFile`)
- Environment variables with `AWTRIXSHARP_` prefix override any JSON value (`AddEnvironmentVariables("AWTRIXSHARP_")`)
- Double-underscore (`__`) used for nesting: e.g. `AWTRIXSHARP_MQTT__HOST`
- .NET User Secrets enabled via `UserSecretsId: 19dd21e0-c64a-420c-9920-c7709d94f4a1` in `src/api/awtrix-api.csproj` (development only)
- `TRANSPORTOPENDATA__APIKEY` read directly via `Environment.GetEnvironmentVariable` (not through the AWTRIXSHARP_ prefix)

**Key config sections:**
- `Awtrix:Devices[]` — list of clock device configs with `BaseTopic` and `Apps[]`
- `Mqtt` — bound to `MqttSettings` (`src/api/Domain/MqttSettings.cs`): `Host`, `Username`, `Password`
- `TransportOpenData:BaseUrl` — optional override for NSW Transport API base URL

**Build:**
- `Dockerfile` — multi-stage build: `mcr.microsoft.com/dotnet/sdk:9.0` build image, `mcr.microsoft.com/dotnet/aspnet:9.0` runtime image
- Build args `GIT_COMMIT` and `GIT_COMMIT_SHORT` baked into assembly metadata via `<AssemblyMetadata>` in `src/api/awtrix-api.csproj`
- `build.ps1` — single-line convenience wrapper: `docker build -t awtrix-sharp .`

## Platform Requirements

**Development:**
- .NET 9 SDK or newer
- Optional: Visual Studio 2022 (sln targets VS 17.14)
- Docker Desktop (for container builds)

**Production:**
- Linux container (`DockerDefaultTargetOS=Linux` in csproj)
- Deployed as `ghcr.io/neutmute/awtrix-sharp` Docker image
- Exposes ports 8080 (HTTP) and 8081 (HTTPS) inside container

---

*Stack analysis: 2026-02-28*
