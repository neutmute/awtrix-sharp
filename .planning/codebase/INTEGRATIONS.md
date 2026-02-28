# External Integrations

**Analysis Date:** 2026-02-28

## APIs & External Services

**Awtrix 3 Smart Clock Devices:**
- Service: Awtrix 3 firmware REST/MQTT API (`https://blueforcer.github.io/awtrix3/#/api`)
- What it's used for: Sending display content (app messages, notifications, settings, sound playback) to physical LED matrix clocks
- SDK/Client: `MQTTnet` 5.0.1.1416 (MQTT path) and `System.Net.Http.HttpClient` (HTTP path)
- Implementation: `src/api/Services/AwtrixService.cs` routes to `MqttPublisher` or `HttpPublisher` based on whether `BaseTopic` starts with `http://`/`https://`
- MQTT topic pattern: `{BaseTopic}/custom/{appName}`, `{BaseTopic}/notify`, `{BaseTopic}/settings`, `{BaseTopic}/rtttl`

**Transport NSW Trip Planner API:**
- Service: NSW Government Open Data Trip Planner REST API
- Base URL: `https://api.transport.nsw.gov.au/v1/tp` (default, overridable via `TransportOpenData:BaseUrl` config)
- What it's used for: Fetching next train departures for `TripTimerApp` (`src/api/Apps/TripTimer/TripTimerApp.cs`)
- SDK/Client: NSwag-generated client `src/transportOpenData/TripPlanner/TripPlannerClient.nswag.cs`; registered via `IHttpClientFactory` in `src/api/Program.cs`
- Auth: API key passed as `Authorization: apikey {key}` header
- Env var: `TRANSPORTOPENDATA__APIKEY`
- Encapsulated in: standalone class library `src/transportOpenData/TransportOpenData.csproj`

**Slack:**
- Service: Slack Socket Mode (WebSocket-based real-time events)
- What it's used for: Receiving `user_change` events to detect presence/status changes and display them via `SlackStatusApp` (`src/api/Apps/SlackStatus/SlackStatusApp.cs`)
- SDK/Client: `SlackNet` 0.17.4 — `ISlackSocketModeClient` via `SlackServiceBuilder`
- Auth: App-level token (`xapp-*`)
- Env var: `AWTRIXSHARP_SLACK__APPTOKEN`
- Implementation: `src/api/HostedServices/SlackConnector.cs` — implements `IHostedService` and `IEventHandler<UserChange>`; auto-reconnects on WebSocket failure with 5-second backoff
- Optional: If `AWTRIXSHARP_SLACK__APPTOKEN` is empty, Slack integration is silently disabled at startup

**MQTT Broker (Home Automation):**
- Service: Any MQTT broker (e.g. Mosquitto, Home Assistant)
- What it's used for: (1) Publishing app messages to Awtrix devices via MQTT; (2) Subscribing to home-automation topics for `MqttRenderApp` and `MqttClockRenderApp` (e.g. solar surplus, room temperature)
- SDK/Client: `MQTTnet` 5.0.1.1416; MQTT protocol version 5.0
- Auth: Optional username/password credentials
- Env vars: `AWTRIXSHARP_MQTT__HOST`, `AWTRIXSHARP_MQTT__USERNAME`, `AWTRIXSHARP_MQTT__PASSWORD`
- Implementation: `src/api/HostedServices/MqttConnector.cs` — singleton, connects on startup, reconnects on publish failure

## Data Storage

**Databases:**
- None — no database dependency. All state is in-memory or sourced from external APIs/MQTT.

**File Storage:**
- `appsettings.json` at `src/api/appsettings.json` — primary runtime configuration (committed to repo, intended as template)
- Test fixtures at `test/transportOpenData.Tests/TestData/*.json` — real JSON API response snapshots for deserialization tests

**Caching:**
- None detected. `TripTimerApp` fetches departures on each cron activation and holds results in-memory for the active window.

## Authentication & Identity

**Auth Provider:**
- No user-facing authentication. The application is a backend service with no login flows.
- Swagger UI is always exposed (no environment guard) — `src/api/Program.cs` lines 80-86
- Device access credentials:
  - MQTT broker: username/password via `MqttSettings`
  - Transport NSW API: API key header
  - Slack: App-level token

## Monitoring & Observability

**Error Tracking:**
- None (no Sentry, Application Insights, or similar SDK)

**Logs:**
- `Microsoft.Extensions.Logging` with `SimpleConsole` formatter: single-line, timestamped (`HH:mm:ss`) output to stdout
- Debug provider also registered (`logging.AddDebug()`)
- Log levels configured in `appsettings.json` under `Logging:LogLevel`
- Assembly git commit baked in at build time and logged at startup (`src/api/Program.cs`: `LogStartup`)

## CI/CD & Deployment

**Hosting:**
- Docker container on any Linux host
- Container image published to GitHub Container Registry: `ghcr.io/neutmute/awtrix-sharp`

**CI Pipeline:**
- GitHub Actions: `.github/workflows/docker-publish.yml`
  - Triggers: push to `master`, `v*.*.*` semver tags, and PRs targeting `master`
  - Push to `master` / tags: builds and pushes image, signs with cosign (Sigstore/Fulcio)
  - PRs: build only, no push, no cosign signing
  - Cache: GitHub Actions cache (`type=gha`)
  - Registry auth: `GITHUB_TOKEN` (automatic)

## Webhooks & Callbacks

**Incoming:**
- None (no inbound webhook endpoints defined in controllers)

**Outgoing:**
- MQTT publishes to `{BaseTopic}/...` topics on the broker (device commands)
- HTTP POST to Awtrix device IP when HTTP transport is selected
- Slack Socket Mode connection is outbound WebSocket initiated by the app

---

*Integration audit: 2026-02-28*
