# AWTRIX NG support: firmware profile design

Date: 2026-09-24. Branch: `feature/ng`. Status: approved by the owner in conversation (Approach 1).

## Goal

Drive AWTRIX NG clocks from AwtrixSharp with the same apps and the same `appsettings.json`
vocabulary that AWTRIX 3 devices use today. AWTRIX 3 support is **transitional**: it stays the
default, keeps working byte-for-byte, and must be deletable later by removing one class.

Non-goals: NG-only features (multiple icons, text fragments beyond colour, palette animation,
scripts), auto-detecting firmware, changing the config vocabulary, Home Assistant discovery.

## Background

AWTRIX NG (https://blueforcer.github.io/awtrix-ng/) has no compatibility layer for AWTRIX 3.
Every topic, HTTP endpoint and payload key this service sends is different, and NG rejects a whole
payload with HTTP 422 when it contains an unknown key. Sources used for the tables below:
`guides/migrating-from-awtrix3/`, `reference/mqtt/`, `reference/http/`, `reference/payload/`,
`advanced/simulator/`.

## Configuration

`AwtrixAddress` gains one property:

```json
{ "BaseTopic": "awtrix/clock2", "Firmware": "NG", "Apps": [ ... ] }
```

- `Firmware` is an enum `AwtrixFirmwareKind { Awtrix3, NG }`; default `Awtrix3`, parsed
  case-insensitively by the configuration binder. Existing configs bind unchanged.
- `BaseTopic` semantics per firmware:
  - AWTRIX 3 (unchanged): MQTT `awtrix/clock1`; HTTP `http://192.168.1.50/api`.
  - NG MQTT: the device's `mqttPrefix` (defaults on the device to its 12-char MAC; set it on the
    clock to something readable, e.g. `awtrix/clock2`).
  - NG HTTP: the device root, e.g. `http://192.168.1.51` or `http://localhost:8080`. A trailing
    `/`, `/api` or `/api/v1` is tolerated and stripped, so copying an AWTRIX 3 style address does
    not double the path.
- An unrecognised `Firmware` string fails configuration binding. `Conductor` binds the whole
  `IOptions<AwtrixConfig>`, so the bad enum value throws when `Conductor` is constructed and the
  service fails to start (fail-fast for the whole service, not a per-device skip).

No other config changes. `ValueMaps` keys and Diurnal `Brightness=` / `GlobalTextColor=` keep
their AWTRIX 3 vocabulary; the NG profile translates.

## Architecture

```
AwtrixApp ──▶ AwtrixService ──▶ AwtrixFirmware.For(address.Firmware)
                                  │  builds AwtrixRequest(Address, Method, Payload)
                                  ▼
                    AwtrixPublisher.Publish(AwtrixRequest)
                        ├─ MqttPublisher   (ignores Method)
                        └─ HttpPublisher   (uses Method; no body on DELETE)
ButtonApp ──▶ AwtrixFirmware.For(...).ButtonTopic(address, button)
```

Everything firmware-specific lives in `src/api/Services/Firmware/`:

| File | Role |
|---|---|
| `AwtrixFirmwareKind.cs` | the enum |
| `AwtrixRequest.cs` | `sealed record AwtrixRequest(string Address, HttpMethod Method, string Payload)` |
| `IAwtrixFirmware.cs` | the profile interface |
| `AwtrixFirmware.cs` | `static IAwtrixFirmware For(AwtrixFirmwareKind)`; profiles are stateless singletons |
| `Awtrix3Firmware.cs` | today's behaviour, moved verbatim |
| `NgFirmware.cs` | NG addressing + `HttpMethod` |
| `NgPayloadTranslator.cs` | `AwtrixAppMessage` → NG JSON (typed) |
| `NgSettingsTranslator.cs` | `AwtrixSettings` → NG settings JSON |

Profiles are pure and stateless, so a static factory is used instead of DI registration; tests
call them directly.

### `IAwtrixFirmware`

```csharp
public interface IAwtrixFirmware
{
    AwtrixFirmwareKind Kind { get; }
    AwtrixRequest AppUpdate(AwtrixAddress address, string appName, AwtrixAppMessage message);
    AwtrixRequest AppClear(AwtrixAddress address, string appName);
    AwtrixRequest Notify(AwtrixAddress address, AwtrixAppMessage message);
    AwtrixRequest Dismiss(AwtrixAddress address);
    AwtrixRequest Settings(AwtrixAddress address, AwtrixSettings settings);
    AwtrixRequest PlayRtttl(AwtrixAddress address, string rtttl);
    string ButtonTopic(AwtrixAddress address, Button button);
}
```

`Method` is only meaningful when `address.IsHttp`; MQTT publishers ignore it. `Payload` is
`string.Empty` for bodiless requests.

### `AwtrixPublisher` change

`Publish(string url, string payload)` becomes `Publish(AwtrixRequest request)`.
`BuildCustomAppUrl` moves out of the publishers into the firmware profiles. `HttpPublisher` sends
`request.Method`; when the payload is empty and the method is `DELETE` no content is attached,
otherwise an empty `application/json` body is sent exactly as today (AWTRIX 3 clear-by-empty-POST
must keep working). `MqttPublisher` publishes `request.Payload` to `request.Address`. The
never-throw contract is unchanged.

### `AwtrixService` change

`ResolvePublisher` stays (HTTP vs MQTT by `BaseTopic`). Each operation now does
`var request = AwtrixFirmware.For(address.Firmware).X(...)` then `SafePublish(address, p => p.Publish(request))`.
The `Notify` rule "blank text ⇒ Dismiss" is unchanged. Null-argument logging is unchanged.

When the NG translator reports dropped keys, `AwtrixService` logs one `Warning` per
`(BaseTopic, key)` for the process lifetime (a `ConcurrentDictionary` set), then `Debug` thereafter,
so a misconfigured ValueMap is visible without flooding the log.

### `ButtonApp` change

`GetTopic(button)` becomes `AwtrixFirmware.For(AwtrixAddress.Firmware).ButtonTopic(AwtrixAddress, button)`.
Payload parsing is unchanged: both firmwares publish `0`/`1`.

## Addressing tables

`{B}` is `BaseTopic` (MQTT) or the normalised HTTP root. `{n}` is the app name, URL-escaped on HTTP.

| Operation | AWTRIX 3 MQTT | AWTRIX 3 HTTP | NG MQTT | NG HTTP |
|---|---|---|---|---|
| AppUpdate | `{B}/custom/{n}` | `POST {B}/custom?name={n}` | `{B}/cmd/apps/pushed/{n}` | `PUT {B}/api/v1/apps/pushed/{n}` |
| AppClear | `{B}/custom/{n}` empty | `POST {B}/custom?name={n}` empty | `{B}/cmd/apps/pushed/{n}` empty | `DELETE {B}/api/v1/apps/{n}` |
| Notify | `{B}/notify` | `POST {B}/notify` | `{B}/cmd/notify` | `POST {B}/api/v1/notifications` |
| Dismiss | `{B}/notify/dismiss` empty | `POST {B}/notify/dismiss` empty | `{B}/cmd/notify/dismiss` empty | `DELETE {B}/api/v1/notifications/active` |
| Settings | `{B}/settings` | `POST {B}/settings` | `{B}/cmd/settings` | `PATCH {B}/api/v1/settings` |
| PlayRtttl | `{B}/rtttl` raw string | `POST {B}/rtttl` raw string | `{B}/cmd/audio/play` `{"rtttl":"…"}` | `POST {B}/api/v1/audio/play` `{"rtttl":"…"}` |
| Button topic | `{B}/stats/button{Left\|Select\|Right}` | n/a | `{B}/state/buttons/{left\|select\|right}` | n/a |

AWTRIX 3 columns are the current code and must not change.

## NG payload translation

Input is an `AwtrixAppMessage` (string-valued dictionary in AWTRIX 3 vocabulary) plus a
`PayloadKind { App, Notification }`. Output is a JSON object with **typed** values (ints, bools,
floats, arrays), because NG validates types. Keys are emitted in a stable order (sorted, `text`
first) so tests can compare strings.

### Colours

NG accepts `#RRGGBB`, `#RGB`, `[r,g,b]`. A 6-hex-digit string without `#` (as `TripTimerApp` sends
in fragments) is normalised to `#RRGGBB`. Int arrays pass through as `[r,g,b]`.

### Key table

| AWTRIX 3 key | NG key | Conversion |
|---|---|---|
| `text` | `text` | string as-is; a JSON fragment array `[{"t","c"}]` becomes `[{"text","color"}]` with colours normalised |
| `textCase` | `textCase` | `0`→`"inherit"`, `1`→`"upper"`, `2`→`"asTyped"`; other → drop |
| `topText` | — | drop |
| `hold` | `hold` | bool (Notification only) |
| `stack` | `stack` | bool (Notification only) |
| `textOffset` | `textOffsetX` | int |
| `center` | `textCenter` | bool |
| `color` | `textColor` | colour |
| `gradient` | `palette` + `textColor:"palette"` | `[[r,g,b],…]` |
| `blinkText` | `textBlinkMs` | int (AWTRIX 3 value is already ms) |
| `fadeText` | `textFadeMs` | int |
| `background` | `backgroundColor` | colour |
| `rainbow` | `palette:"Rainbow"` + `textColor:"palette"` | only when `true`; `false` → drop |
| `icon` | `icon` | string |
| `pushIcon` | `iconMode` | `0`→`"fixed"`, `1`→`"pushOnce"`, `2`→`"push"` |
| `duration` | `durationMs` | seconds × 1000 |
| `line` | `lineChart` | int array |
| `bar` | `barChart` | int array |
| `autoscale` | `chartAutoscale` | bool |
| `lifetime` | `lifetimeMs` | seconds × 1000 (App only) |
| `lifetimeMode` | `lifetimeExpiry` | `0`→`"remove"`, `1`→`"mark"` (App only) |
| `overlay` | `overlay` | string |
| `progress` | `progress` | int |
| `progressC` | `progressColor` | colour array |
| `progressBC` | `progressTrackColor` | colour array |
| `scrollSpeed` | `scroll` | `{"speed": N}` |
| `effect` | `effect` | string pass-through (NG effect names differ; a bad name yields a 422 the publisher logs) |
| `effectSpeed` | `effectSpeed` | number |
| `effectPalette` | `palette` | string |
| `effectBlend` | `paletteBlend` | bool |

Rules:
- Palette precedence when several sources set it: `gradient` > `rainbow` > `effectPalette`.
- `textColor:"palette"` overrides an explicit `color` when a palette is set (matches AWTRIX 3 where
  gradient/rainbow override colour).
- Keys marked *Notification only* are dropped from App payloads and vice versa (NG returns 422
  otherwise). Dropped-because-of-kind is `Debug`, dropped-because-unsupported is reported to the
  caller (see the `AwtrixService` warning rule).
- Unknown keys (anything not in the table) are dropped and reported.
- Unparsable values (e.g. `duration` = `abc`) are dropped and reported, never emitted.
- A message that translates to `{}` is still sent for App/Notify: NG rejects `{}` on pushed apps
  with 422, which the publisher logs; this mirrors an empty AWTRIX 3 message doing nothing useful.

### Settings

| AWTRIX 3 | NG |
|---|---|
| `BRI` | `brightness` (int) |
| `TCOL` | `textColor` (colour) |
| other | dropped and reported |

## Error handling

Unchanged contract: publishers never throw; `AwtrixService.SafePublish` still contains any
surprise. New: `HttpPublisher` logs the response body at `Warning` on 4xx (NG's 422 names the
offending field), truncated to 512 chars. AWTRIX 3 devices rarely return bodies, so this is
harmless there.

## Simulator environment

Purpose: run the real service against the NG simulator on this machine, never the real clock.

- `src/api/appsettings.Simulator.json`: `Mqtt:Host` `localhost`; one device
  `{ "BaseTopic": "http://localhost:8080", "Firmware": "NG", "Apps": [DiurnalApp, MqttClockRenderApp] }`
  using the same app configs as `appsettings.json` minus TripTimer and Slack (they need secrets).
  The file is self-contained: configuration arrays merge by index, so the base file must not load.
- Safety relies on `WebApplication.CreateBuilder` loading user secrets **only in Development**.
  In the Simulator environment the `AWTRIXSHARP_` environment-variable provider is not added and the
  base `appsettings.json` is not loaded. `SimulatorEnvironmentTests` builds the host with
  `EnvironmentName = "Simulator"` and pins both (plus no user-secrets source, and a single simulator
  device with only `DiurnalApp` and `MqttClockRenderApp`); `Production_StillLoadsAwtrixSharpPrefixedVariables`
  proves the skip is Simulator-only. The Slack (`AWTRIXSHARP_SLACK__*`), Data
  (`AWTRIXSHARP_SETTINGS__DATA_DIRECTORY`) and TransportOpenData (`TRANSPORTOPENDATA__APIKEY`) literal
  environment-variable fallbacks in `Program.AddSettings` are disabled the same way in the Simulator
  environment, pinned by `SimulatorEnvironmentTests`.
- `docs/simulator.md` runbook: build the simulator (`pio run -e native_sim`), run it, optional local
  Mosquitto, `dotnet run --project src/api --launch-profile Simulator` (a plain `dotnet run` uses the
  first launch profile, which pins `ASPNETCORE_ENVIRONMENT=Development`), what to look for
  (pushed app on the preview grid, `GET /api/v1/apps`, 422 bodies in the service log).
- The owner's "never run locally" rule is relaxed only for this environment.
- PlatformIO is not installed on the implementation machine, so the simulator run is a manual
  verification step for the owner; everything else is proven by unit tests.

## Testing

- **AWTRIX 3 regression:** existing `AwtrixServicePublishTests`, `HttpPublisherTests`,
  `MqttPublisherTests`, `AwtrixAppMessageJsonTests` keep passing with only the mechanical change
  from `Publish(url, payload)` to `Publish(AwtrixRequest)`; assertions on topics and bodies are
  unchanged.
- **`Awtrix3FirmwareTests`:** every row of the addressing table, both transports.
- **`NgFirmwareTests`:** every row of the addressing table, both transports, including HTTP root
  normalisation (`/`, `/api`, `/api/v1` suffixes) and app-name escaping.
- **`NgPayloadTranslatorTests`:** one test per key-table row, palette precedence, kind filtering,
  fragment conversion, colour normalisation, unparsable values, stable key order, exact JSON.
- **`NgSettingsTranslatorTests`.**
- **`AwtrixServiceTests`:** NG device routes through the NG profile; dropped-key warning is
  logged once per device+key.
- **`ButtonAppTests`:** NG device subscribes to `state/buttons/left|select|right`.
- **`AwtrixAddressTests`:** `Firmware` binds from JSON case-insensitively and defaults to `Awtrix3`.
- **Simulator environment test** as described above.
- **`HttpPipelineTests` / DI composition test** still pass.

## Documentation

- `README.md`: a "AWTRIX NG" section: the `Firmware` setting, `BaseTopic` semantics per firmware,
  the key-translation table (or a link to this spec), known gaps (effect names, NG-only features).
- `CLAUDE.md`: one paragraph on the firmware profile seam and the Simulator environment.
- `docs/simulator.md` runbook.

## Deleting AWTRIX 3 later

Remove `Awtrix3Firmware.cs`, its tests, the enum member and the default; make `NG` the default.
Nothing else references AWTRIX 3 specifics. This is the check that the seam is in the right place.
