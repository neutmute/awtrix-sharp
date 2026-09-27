# Test UI: app test panel and visuals playground

Date: 2026-09-27. Branch: `feature/test-ui`. Status: design approved by the owner in conversation;
owner asked for spec, plan and execution to proceed unattended.

## Goal

A browser UI served by the existing ASP.NET service, in the same Docker image, for two jobs:

1. **App test panel** (`/ui`): for each configured clock, list its apps, run any of them now, see the
   last payload each app pushed, and dismiss/clear the clock between tests.
2. **Visuals playground** (`/ui/visuals`): compose a test notification or custom-app update using the
   AWTRIX NG visual features (effects, palettes, overlays, transitions, colours, text options), see the
   NG JSON it produces, hand-edit it, and send it to a chosen clock.

Dropdown vocabularies come from the clock itself over MQTT (`{baseTopic}/state/capabilities`) with a
built-in fallback taken from https://blueforcer.github.io/awtrix-ng/reference/visuals/.

Non-goals: authentication for the UI (it is open like Swagger), saving user presets, persistence of
any kind, live pixel preview of the clock, a `Ui:Enabled` toggle, editing device configuration.

## 1. Technology and hosting

**Blazor Server** (Razor components with the interactive server render mode), because it compiles in
the existing `dotnet publish`, needs no Node toolchain or Docker change, reuses the DI services and the
`AwtrixAppMessage` builder directly, and is unit-testable with bUnit.

- Components live in `src/api/Ui/`: `App.razor` (root document), `Routes.razor`, `Layout/UiLayout.razor`,
  `Pages/AppTestPanel.razor` (`@page "/ui"`), `Pages/VisualsPlayground.razor` (`@page "/ui/visuals"`),
  plus small child components under `Components/`.
- `Program.AddHttpSurface` adds `services.AddRazorComponents().AddInteractiveServerComponents()`.
  `Program.ConfigureHttpPipeline` calls `app.UseStaticFiles()` (or `MapStaticAssets`) and
  `app.MapRazorComponents<App>().AddInteractiveServerRenderMode()` **before** `UseMiddleware<ApiKeyMiddleware>()`,
  so the UI and its `/_blazor` circuit stay reachable when `Api:Key` is set, exactly like Swagger.
  Because endpoint routing resolves the endpoint before middleware runs but the middleware still executes
  for every request, the middleware must skip requests whose path starts with `/ui` or `/_blazor` or
  `/_framework`. Implement that as an explicit path check in `ApiKeyMiddleware` (tested), not by ordering
  alone.
- Styling: one hand-written stylesheet `wwwroot/ui/site.css` (dark theme, monospace for JSON, simple
  grid). No CSS framework, no CDN dependency, so the container works offline on a LAN.
- The `awtrix-api.csproj` needs no new package for Blazor Server (it is part of the shared framework).
  The test project adds `bunit` (latest 1.x stable) for component tests.
- Swagger stays the default landing page; the layout links to `/swagger` and Swagger is not changed.

## 2. New services

### 2.1 `NgVisuals` (Domain)

`src/api/Domain/NgVisuals.cs`: static, immutable string arrays copied from the NG visuals reference:
`Effects` (19), `PaletteEffects` (15, per the capabilities payload), `Transitions` (22), `Overlays` (6),
`Palettes` (8), `TransitionDirections` (`normal`, `reverse`), `Fonts` (`small`, `large`),
`ScrollModes` (`static`, `wrap`, `loop`, `bounce`). Also `EffectSpeedMin = 0.1`, `EffectSpeedMax = 10`.

### 2.2 `DeviceCapabilities` (Domain)

`src/api/Domain/DeviceCapabilities.cs`: a record with `string[] Effects, PaletteEffects, Transitions,
Overlays, Palettes` and a `Source` enum (`Device`, `BuiltIn`). `DeviceCapabilities.BuiltIn` is the
fallback built from `NgVisuals`. `TryParse(string json, out DeviceCapabilities)` deserialises the
capabilities payload (unknown keys such as `audio`, `gpio`, `scriptUpdates` are ignored; a missing
array becomes the built-in list for that array). Malformed JSON returns false.

### 2.3 `DeviceStateMonitor` (HostedServices)

`src/api/HostedServices/DeviceStateMonitor.cs`, registered as a singleton and as a hosted service
**after** `MqttConnector` and before `Conductor` (order matters only for subscription registration;
the connector defers subscriptions while disconnected, so this is not a correctness dependency).

- On `StartAsync`: for every device in `AwtrixConfig.Devices` whose address is not HTTP, subscribe to
  `{baseTopic}/state/capabilities`, `{baseTopic}/state/settings`, `{baseTopic}/state/device`, and
  attach one handler to `IMqttConnector.MessageReceived`.
- Handler: match the topic against the three suffixes for a known device (ordinal). Store the raw
  payload string with a UTC timestamp per (device, kind). For `capabilities`, also run `TryParse`; on
  failure log a warning and keep the previous parsed value. Never throw.
- API:
  - `DeviceState Get(string baseTopic)` returning `DeviceCapabilities Capabilities` (built-in fallback
    when nothing received or the device is HTTP), `string? SettingsJson`, `string? DeviceJson`,
    `DateTimeOffset? LastSeen` (latest of the three).
  - `event Action<string>? Changed` raised with the base topic after a state update; pages subscribe
    and marshal to the renderer with `InvokeAsync(StateHasChanged)`.
- `StopAsync` detaches the handler. Subscriptions are left registered (the connector owns them).
- The Simulator device in `appsettings.Simulator.json` is HTTP, so the monitor subscribes to nothing
  there and the playground shows built-in lists. That is expected and documented.

### 2.4 `PublishTrace` (Services)

`src/api/Services/PublishTrace.cs`, singleton. Records every request the `AwtrixService` sends.

- `record PublishRecord(DateTimeOffset At, string BaseTopic, string Operation, string? AppName,
  AwtrixRequest Request, bool Delivered)`. `Operation` is one of `Notify`, `Dismiss`, `AppUpdate`,
  `AppClear`, `Settings`, `Rtttl`.
- `void Record(PublishRecord record)`: appends to a bounded ring buffer (capacity 50, `internal`
  constructor parameter for tests) and updates a dictionary keyed by `(BaseTopic, AppName)` holding the
  last `AppUpdate`/`AppClear` record for that app. Thread-safe (lock).
- `IReadOnlyList<PublishRecord> Recent()` newest first; `PublishRecord? LastForApp(string baseTopic,
  string appName)`; `event Action? Changed`.
- `AwtrixService` takes `PublishTrace` as an optional constructor parameter (default `null` keeps the
  many existing test constructions compiling) and records after every `SafePublish`, including failures
  (`Delivered=false`). `TimeProvider` is injected for timestamps (already registered).
- App names: apps publish under their config `Name`; the service already receives `appName` for
  `AppUpdate`/`AppClear`, so the record uses it verbatim. Notify/Dismiss records carry `AppName = null`.

### 2.5 `AwtrixAppMessage.FromJson`

`static bool TryFromJson(string json, out AwtrixAppMessage message, out string? error)`: parses a JSON
object into a message whose values are `JsonElement`s (the serializer round-trips them unchanged).
Non-object JSON or a parse failure returns false with the parser message. This powers the playground's
manual JSON mode while keeping every send on the same `IAwtrixService` path (key exclusion, blank text
→ dismiss, tracing).

### 2.6 `IMqttConnector.IsConnected`

Add `bool IsConnected { get; }` to the interface (only `MqttConnector` implements it; tests use Moq)
so the layout can show broker state.

## 3. App test panel (`/ui`)

- **Devices** from `IOptions<AwtrixConfig>`. One card per device: base topic, transport badge (MQTT /
  HTTP), and, when the monitor has a device payload, a one-line summary parsed leniently from the
  device JSON (show `version`/`firmware` and `ip`-like keys if present, otherwise "state received at
  HH:mm:ss"). Raw device JSON is behind a "details" disclosure.
- **App rows**: for each `AppConfig` on the device: `Type`, a compact `key=value` list of `Config`,
  and the `ValueMaps` (matcher plus the non-null overrides). A **Run now** button calls
  `Conductor.ExecuteNow(baseTopic, appType)` and renders the `AppExecutionResult` inline as
  `Started` / `Not running` / `Error`.
- **Last payload** under each row from `PublishTrace.LastForApp(baseTopic, appType)`: time, operation,
  method and address, pretty-printed JSON payload. Else "nothing sent yet".
- **Device actions**: **Dismiss notification** (`IAwtrixService.Dismiss`) and **Clear app** with a text
  field defaulting to the first app's name (`IAwtrixService.AppClear`). Each shows delivered / not
  delivered.
- **Recent sends**: collapsible list of `PublishTrace.Recent()` across devices, newest first, each row
  expandable to show the payload.
- The page subscribes to `PublishTrace.Changed` and `DeviceStateMonitor.Changed` in `OnInitialized`
  and unsubscribes in `Dispose`.

## 4. Visuals playground (`/ui/visuals`)

- **Target**: device select (defaults to the first device), radio `Notify` / `Custom app`; the latter
  shows an app name field defaulting to `test`.
- **Form model** `VisualsForm` (a plain class in `src/api/Ui/Models/`, unit-testable without bUnit):
  - Text: `Text` (default "Awtrix Sharp!"), `TextColor` (`#FFFFFF`), `BackgroundColor` (blank), `TextCase`
    (`Inherit`), `Icon` (blank), `IconMode` (`Fixed`), `Font` (blank).
  - Effect: `Effect` (blank), `EffectSpeed` (1.0), `Palette` (blank), `PaletteStops` (blank, `"R,G,B;R,G,B"`
    format already supported by `AwtrixAppMessage.SetPalette(int[][])` via `TryParseIntRows`),
    `PaletteBlend` (true).
  - `Overlay` (blank).
  - Timing: `DurationMs` (5000), `LifetimeMs` (0 = unset), `Hold` (false), `TextBlinkMs` (0), `TextFadeMs` (0).
  - Scroll: `ScrollMode` (blank), `ScrollSpeed` (0 = unset).
  - Transition (Custom app only): `TransitionEffect`, `TransitionDirection`, `TransitionDurationMs` (0 =
    unset). These are set as raw keys `transitionEffect`, `transitionDirection`, `transitionDurationMs`
    on the message (add matching setters to `AwtrixAppMessage`).
  - `AwtrixAppMessage Build()`: only non-blank / non-zero fields are set. `Font` sets key `font`.
    `ScrollMode` sets `scroll` as `{ "mode": ..., "speed": ... }` (extend `SetScrollSpeed` with a
    `SetScroll(string? mode, int? speed)` setter). Colours go through `AwtrixColour.TryParse`; a bad
    colour is reported in `Validate()` as a field error, and `Build()` skips it.
  - `IReadOnlyList<string> Validate()` returns human-readable errors (bad colour, bad palette stops,
    effect speed outside 0.1..10, transition fields set while target is Notify).
- **Dropdowns**: an editable combo pattern: `<input list="...">` with a `<datalist>` fed by
  `DeviceCapabilities` for the selected device, so a typed value not in the list is accepted. A caption
  under the group says "lists from device" or "built-in lists (no capabilities received)".
- **Live JSON**: the right column shows `form.Build().ToJson()` pretty-printed. Editing the text area
  switches the page to **manual** mode: the form is disabled, the JSON is what will be sent, and a
  "Back to form" button restores form mode (regenerating JSON from the form). In manual mode the JSON is
  parsed with `AwtrixAppMessage.TryFromJson` on every change; a failure shows the error and disables Send.
- **Actions**: **Send** (`Notify` or `AppUpdate` according to target), **Dismiss**, **Clear this app**
  (Custom app only). Result line shows delivered / not delivered plus the request address from the
  trace.
- **Presets**: a static list in `VisualsPresets` of five entries, each a name and an `Action<VisualsForm>`:
  "Plasma + Rainbow", "Matrix green", "Snow overlay, hold", "Fireworks, fast", "Palette text bounce".
  Loading a preset resets the form then applies the action and returns to form mode.
- **Device state**: collapsible section with the raw settings JSON and device JSON from the monitor,
  with their received time.

## 5. Error handling

- Broker down: the layout header shows "MQTT: connected/disconnected". Sends to MQTT devices return
  false; the pages say "not delivered (see logs)". HTTP devices are unaffected.
- No capabilities yet: built-in lists with the caption above.
- Malformed state payloads: warning log, previous value kept.
- Every send goes through `IAwtrixService`, which never throws; components still wrap calls so an
  unexpected exception renders as an error line instead of killing the circuit.

## 6. Tests (all in `test/Test`)

- `Domain/DeviceCapabilitiesTests`: parses the real capabilities payload (fixture file under
  `test/Test/TestData/ng-capabilities.json`); ignores extra keys; missing array falls back; malformed
  returns false; `BuiltIn` matches `NgVisuals`.
- `HostedServices/DeviceStateMonitorTests` with `Mock<IMqttConnector>`: subscribes to the three topics
  per MQTT device and none for HTTP devices; a capabilities message updates `Get()`; settings/device
  raw JSON stored; malformed capabilities keeps previous; unrelated topic ignored; `Changed` fires with
  the base topic; handler never throws.
- `Services/PublishTraceTests`: capacity, newest-first order, `LastForApp` by device and app, failure
  records included, `Changed` fires.
- `Services/AwtrixServiceTraceTests`: `Notify`, `AppUpdate`, `AppClear`, `Dismiss` each record once
  with the right operation and app name; a publisher returning false records `Delivered=false`.
- `Domain/AwtrixAppMessageFromJsonTests`: round-trip, non-object rejected, invalid JSON error text.
- `Ui/VisualsFormTests`: defaults build the minimal payload; each field maps to its key; blank/zero
  fields are omitted; validation errors; transition keys only for Custom app target; presets build
  without validation errors.
- `Ui/AppTestPanelTests` (bUnit): renders a device card per configured device with its apps; Run now
  shows `Started`; last payload shown after a trace record; Dismiss calls the service.
- `Ui/VisualsPlaygroundTests` (bUnit): JSON preview follows the form; manual mode disables the form and
  bad JSON disables Send; Send calls `Notify` or `AppUpdate` with the built message; capabilities from
  the monitor populate the datalists; fallback caption shown for an HTTP device.
- `Middleware/ApiKeyMiddlewareTests`: `/ui`, `/_blazor/...` and `/_framework/...` pass without a key
  while `/diagnostics` is rejected. Plus an `Http` integration test that `GET /ui` returns 200 with
  `Api:Key` configured (using the existing TestHost pattern).

## 7. Docs

- `docs/ui.md`: what the two pages do, the MQTT state topics used, the built-in fallback, and that the
  UI is open like Swagger.
- README: one paragraph and a link under the existing Swagger mention.
- CLAUDE.md: add `DeviceStateMonitor` and `PublishTrace` to the key classes table and a line noting the
  UI lives in `src/api/Ui`.

## 8. Manual verification

Run with the Simulator launch profile only (per the standing rule; never Development). Open
`http://localhost:5115/ui` and `/ui/visuals`, run the Diurnal and MqttClockRender apps, send a Plasma
notification to the simulator. The capabilities path cannot be exercised against the simulator (HTTP
device), so it is covered by unit tests with the fixture payload the owner supplied.
