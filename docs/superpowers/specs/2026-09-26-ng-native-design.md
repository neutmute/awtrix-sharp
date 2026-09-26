# NG-native: remove AWTRIX 3 support and the translation layer

Date: 2026-09-26. Branch: `feature/ng-native`. Status: approved by the owner in conversation.

## Goal

Every device now runs AWTRIX NG. Delete AWTRIX 3 support and the AWTRIX 3 → NG translation layer
introduced by `docs/superpowers/specs/2026-09-24-awtrix-ng-firmware-design.md`. The service speaks NG
natively: `AwtrixAppMessage`, `AwtrixSettings`, the ValueMap vocabulary and the Diurnal settings use NG
payload names and typed values, and there is exactly one addressing table.

This is a **breaking configuration change**. Existing `appsettings.json` files are converted with the
migration skill described in §7; the service does not translate old names.

Non-goals: new NG features (multiple icons, palette animation, scripts, text fragment fields beyond
`text`/`color`), Home Assistant discovery, auto-detecting firmware.

## 1. Message model

`src/api/Domain/AwtrixAppMessage.cs`

```csharp
public class AwtrixAppMessage : Dictionary<string, object?>
```

Keys are NG payload names (https://blueforcer.github.io/awtrix-ng/reference/payload/). Values are
stored typed by the fluent setters, so `ToJson` is a plain `JsonSerializer.Serialize` (relaxed JSON
escaping, `text` first then keys sorted ordinally, so tests can compare strings). Only keys that were
set are emitted; NG rejects unknown keys and treats absent keys as defaults, so no setter has a default.

Setters (all return `this`):

| Setter | Key | Stored value |
|---|---|---|
| `SetText(string)` | `text` | string |
| `SetText(IEnumerable<TextFragment>)` | `text` | `TextFragment[]` (serializes as `[{"text","color"}]`; `color` omitted when null) |
| `SetTextCase(TextCase)` | `textCase` | enum → `inherit` / `upper` / `asTyped` |
| `SetHold(bool = true)` | `hold` | bool |
| `SetStack(bool = true)` | `stack` | bool |
| `SetTextOffsetX(int)` | `textOffsetX` | int |
| `SetTextCenter(bool)` | `textCenter` | bool |
| `SetTextColor(string)` | `textColor` | colour (see §1.1) or the literal `palette` |
| `SetBackgroundColor(string)` | `backgroundColor` | colour |
| `SetPalette(string)` | `palette` | palette name, e.g. `Rainbow` |
| `SetPalette(int[][])` | `palette` | `[[r,g,b],…]` |
| `SetPaletteBlend(bool)` | `paletteBlend` | bool |
| `SetTextBlinkMs(int)` | `textBlinkMs` | int |
| `SetTextFadeMs(int)` | `textFadeMs` | int |
| `SetIcon(string)` | `icon` | string |
| `SetIconMode(IconMode)` | `iconMode` | enum → `fixed` / `pushOnce` / `push` |
| `SetDurationMs(int)` / `SetDuration(TimeSpan)` | `durationMs` | int |
| `SetLifetimeMs(int)` / `SetLifetime(TimeSpan)` | `lifetimeMs` | int |
| `SetLifetimeExpiry(LifetimeExpiry)` | `lifetimeExpiry` | enum → `remove` / `mark` |
| `SetLineChart(int[])` | `lineChart` | int[] |
| `SetBarChart(int[])` | `barChart` | int[] |
| `SetChartAutoscale(bool)` | `chartAutoscale` | bool |
| `SetOverlay(string)` | `overlay` | string |
| `SetProgress(int)` | `progress` | int |
| `SetProgressColor(string)` | `progressColor` | colour |
| `SetProgressTrackColor(string)` | `progressTrackColor` | colour |
| `SetScrollSpeed(int)` | `scroll` | `{ "speed": n }` |
| `SetEffect(string)` | `effect` | string |
| `SetEffectSpeed(double)` | `effectSpeed` | number |

`Text` getter returns the string value, or null when unset or a fragment array. `ToString()` stays a
`key=value` listing (`text` first) for logs and test messages; fragment and array values print as
their JSON.

`TextCase`, `IconMode`, `LifetimeExpiry` are enums in `AwtrixSharpWeb.Domain`, serialized with a
`JsonStringEnumConverter` using camelCase names. `TextFragment` is
`public sealed class TextFragment(string text, string? color = null)` exposing `string Text` and
`object? Color` (the §1.1 parse result, so a bad colour throws from the constructor).

`SetPalette(string)` and `SetTextColor("palette")` are independent. NG only colours text from the
palette when `textColor` is `palette`, so callers (and configs) that want rainbow text set both.

### 1.1 Colours

`src/api/Domain/AwtrixColour.cs` (today's `NgColour`, moved and renamed):

`public static object Parse(string value)` accepts `#RRGGBB`, `#RGB` (returned as the string, `#`
preserved) and `r,g,b` (returned as `int[3]`, each 0–255). Anything else, including bare `RRGGBB`,
throws `ArgumentException`. `TryParse` wraps it. Colour setters call `Parse`, so an invalid colour
throws from the setter; `ValueMapSetters` catches that and reports "invalid value".

### 1.2 Settings

`src/api/Domain/AwtrixSettings.cs`: `Dictionary<string, object?>` with `SetBrightness(byte)` →
`brightness` and `SetTextColor(string)` → `textColor` (colour). `ToJson` serializes sorted.

## 2. Endpoints and transport

Delete `src/api/Services/Firmware/` entirely. Replace with:

- `src/api/Services/AwtrixRequest.cs`: `sealed record AwtrixRequest(string Address, HttpMethod Method, string Payload)`
  with `static Post(address, payload)`. `DroppedKeys` is gone.
- `src/api/Services/AwtrixEndpoints.cs`: static class, the body of today's `NgFirmware` with these
  changes: no `Kind`; `HttpRoot(baseTopic)` is `baseTopic.TrimEnd('/')` only (no `/api` or `/api/v1`
  stripping); `AppUpdate`/`Notify` take the JSON string, not the message, so the class has no payload
  knowledge beyond `PlayRtttl`'s `{"rtttl":…}` and `Settings` taking `AwtrixSettings.ToJson()`.

Addressing table (unchanged from the NG column of the previous spec). `{B}` is `BaseTopic` (MQTT) or
`BaseTopic` with trailing `/` trimmed (HTTP); `{n}` is the app name, URL-escaped on HTTP:

| Operation | MQTT | HTTP |
|---|---|---|
| AppUpdate | `{B}/cmd/apps/pushed/{n}` | `PUT {B}/api/v1/apps/pushed/{n}` |
| AppClear | `{B}/cmd/apps/pushed/{n}` empty | `DELETE {B}/api/v1/apps/{n}` |
| Notify | `{B}/cmd/notify` | `POST {B}/api/v1/notifications` |
| Dismiss | `{B}/cmd/notify/dismiss` empty | `DELETE {B}/api/v1/notifications/active` |
| Settings | `{B}/cmd/settings` | `PATCH {B}/api/v1/settings` |
| PlayRtttl | `{B}/cmd/audio/play` `{"rtttl":"…"}` | `POST {B}/api/v1/audio/play` `{"rtttl":"…"}` |
| Button topic | `{B}/state/buttons/{left\|select\|right}` | n/a |

`AwtrixService`:
- `SafePublish(address, Func<AwtrixRequest>)`: no firmware lookup, no `_warnedDroppedKeys`, no
  dropped-key logging. Never-throw contract unchanged.
- `AppUpdate` serializes the message **without** `hold` and `stack`; `Notify` serializes it
  **without** `lifetimeMs` and `lifetimeExpiry`. A removed key is logged at Debug. This keeps a
  ValueMap shared between a pushed app and a notification from producing a 422. Implemented as
  `AwtrixAppMessage.ToJson(params string[] excludedKeys)` or an equivalent internal helper.
- Doc comments point at the NG references, not `awtrix3`.

`AwtrixAddress`: the `AwtrixFirmwareKind Firmware` property is replaced by
`public string? Firmware { get; set; }`, documented as a migration tripwire: it is never read except
by `ConfigurationWarnings`, which adds one warning per device where it is non-empty
("Device {BaseTopic}: the Firmware setting is obsolete, every device is AWTRIX NG; remove it (see
docs/config-migration.md)").

`ButtonApp.GetTopic` calls `AwtrixEndpoints.ButtonTopic(AwtrixAddress, button)`.

`HttpPublisher` / `MqttPublisher`: namespace import changes only; the "AWTRIX 3 clears an app with an
empty POST" comment becomes "an empty body is still sent for non-DELETE verbs".

## 3. Config vocabulary

`ValueMapSetters` is re-keyed to the NG names. Keys (case-insensitive) and parsing:

| ValueMap key | Setter | Value format |
|---|---|---|
| `Text` | `SetText(string)` | string |
| `TextCase` | `SetTextCase` | `inherit` / `upper` / `asTyped` (case-insensitive) |
| `Hold`, `Stack`, `TextCenter`, `PaletteBlend`, `ChartAutoscale` | bool setters | `true` / `false` |
| `TextOffsetX`, `TextBlinkMs`, `TextFadeMs`, `DurationMs`, `LifetimeMs`, `Progress`, `ScrollSpeed` | int setters | integer |
| `TextColor` | `SetTextColor` | colour or `palette` |
| `BackgroundColor`, `ProgressColor`, `ProgressTrackColor` | colour setters | colour |
| `Palette` | `SetPalette(string)` or `SetPalette(int[][])` | palette name, or `r,g,b;r,g,b` (chosen by whether the value parses as a matrix) |
| `Icon`, `Overlay`, `Effect` | string setters | string |
| `IconMode` | `SetIconMode` | `fixed` / `pushOnce` / `push` |
| `LifetimeExpiry` | `SetLifetimeExpiry` | `remove` / `mark` |
| `LineChart`, `BarChart` | int-array setters | `1,2,3` |
| `EffectSpeed` | `SetEffectSpeed` | number |

The existing coverage test (every public single-argument `Set*` has an entry) is kept; the `TimeSpan`
overloads and `SetText(IEnumerable<TextFragment>)` are excluded by name.

The "Unknown key" problem message becomes
`Unknown key '{key}' is ignored (AWTRIX 3 name? see docs/config-migration.md)`.

`DiurnalSchedule` accepts `Brightness=` (byte) and `TextColor=` (colour). `GlobalTextColor=` is
unknown and gets the existing "unknown setting" warning, which now reads
`(expected Brightness or TextColor; see docs/config-migration.md)`. An invalid colour is warned and
skipped like an invalid brightness.

`src/api/appsettings.json`, `src/api/appsettings.Simulator.json`, `test/Test/appsettings.json` and every
README example are converted to the new vocabulary (`Firmware` removed, `Color` → `TextColor`,
`Background` → `BackgroundColor`, `Duration: "60"` → `DurationMs: "60000"`,
`GlobalTextColor=` → `TextColor=`).

## 4. Apps

- `TripTimerApp.BuildMessage`: fragments become
  `SetText(new[] { new TextFragment(clockText, nowColor), new TextFragment($" ->{nextAlarm:mm}", "#FF0000") })`
  with `nowColor` `#00FF00` / `#FFA500`. The no-ValueMap fallback is
  `SetText("GO!").SetPalette("Rainbow").SetTextColor("palette").SetProgress(100)`.
  `SetDuration(300)` → `SetDuration(TimeSpan.FromMinutes(5))`.
- `SlackStatusApp`: `SetDuration(TimeSpan.FromSeconds(DefaultDurationSeconds))`.
- `MqttClockRenderApp`: `SetDuration(TimeSpan.FromHours(1))`.
- No other app code changes.

## 5. Error handling

Unchanged: publishers never throw, `SafePublish` contains everything, `HttpPublisher` logs 4xx
bodies (NG names the offending field). Invalid config values are reported once at startup by the
existing ValueMap / Diurnal problem reporting and skipped at runtime.

## 6. Tests

Delete: `test/Test/Services/Firmware/Awtrix3FirmwareTests.cs`, `NgPayloadTranslatorTests.cs`,
`NgSettingsTranslatorTests.cs`.

Rename/rewrite:
- `NgFirmwareTests` → `test/Test/Services/AwtrixEndpointsTests.cs`: every table row, both transports,
  app-name escaping, trailing-slash trimming, and a test that `http://host/api` is **not** stripped.
- `NgColourTests` → `test/Test/Domain/AwtrixColourTests.cs`: the three accepted forms, bare hex rejected.
- `AwtrixAppMessageJsonTests`, `AwtrixAppMessageBuilderTests`, `AwtrixAppMessageTest`,
  `AwtrixSettingsTests`: NG keys, typed values, enum names, fragment serialization, `scroll` nesting,
  key order, `Text` getter with fragments, invalid colour throws.
- `ValueMapTests` / `ValueMapSettersTests`: new keys, enum-by-name parsing, `Palette` name vs matrix,
  invalid colour reported as invalid value, unknown AWTRIX 3 key (`Color`) reported with the hint.
- `AwtrixServiceTests` / `AwtrixServicePublishTests`: NG addresses and bodies; `AppUpdate` strips
  `hold`/`stack`; `Notify` strips `lifetimeMs`/`lifetimeExpiry`; dropped-key warning tests removed.
- `AwtrixAddressTests`: `Firmware` binds as an optional string; `ConfigurationWarnings` warns when set.
- `ButtonAppTests`: NG topics only.
- `DiurnalScheduleTests`: `TextColor=` accepted, `GlobalTextColor=` warned.
- `TripTimerAppTests`: fragment array and rainbow fallback assertions.
- `SimulatorEnvironmentTests`: unchanged apart from the sample file no longer carrying `Firmware`.

`dotnet build` warning-free for the touched files and `dotnet test` green before push.

## 7. Migration support

`.claude/skills/awtrix3-config-migration/SKILL.md` (project skill; frontmatter `name`, `description`
that triggers on "convert / migrate this config", "awtrix3 config", "old appsettings"). Contents:

1. Procedure: read the pasted or referenced JSON; apply the table; report every key with no
   equivalent; output the full converted JSON plus a bullet list of what changed and what was dropped;
   never invent values.
2. Device level: remove `Firmware`; HTTP `BaseTopic` loses a trailing `/api` or `/api/v1` and any
   trailing `/`; MQTT `BaseTopic` unchanged.
3. ValueMap and message key table:

| AWTRIX 3 | NG | Value conversion |
|---|---|---|
| `Text` (string) | `Text` | as-is |
| `Text` (`[{"t","c"}]` JSON) | not expressible in a ValueMap | report; only `TripTimerApp` produced these and it now builds them in code |
| `TextCase` 0/1/2 | `TextCase` | `inherit` / `upper` / `asTyped` |
| `TopText` | — | drop and report |
| `Hold`, `Stack` | same | as-is |
| `TextOffset` | `TextOffsetX` | as-is |
| `Center` | `TextCenter` | as-is |
| `Color` | `TextColor` | `RRGGBB` → `#RRGGBB`; `#…` and `r,g,b` as-is |
| `Background` | `BackgroundColor` | colour as above |
| `Gradient` `r,g,b;r,g,b` | `Palette` + `TextColor: palette` | matrix as-is |
| `Rainbow: true` | `Palette: Rainbow` + `TextColor: palette` | `false` → drop |
| `EffectPalette` | `Palette` | as-is; loses to Gradient and Rainbow when several are present |
| `BlinkText`, `FadeText` | `TextBlinkMs`, `TextFadeMs` | round to int |
| `Icon` | `Icon` | as-is |
| `PushIcon` 0/1/2 | `IconMode` | `fixed` / `pushOnce` / `push` |
| `Duration` (s) | `DurationMs` | × 1000 |
| `Lifetime` (s) | `LifetimeMs` | × 1000 |
| `LifetimeMode` 0/1 | `LifetimeExpiry` | `remove` / `mark` |
| `Line`, `Bar` | `LineChart`, `BarChart` | as-is |
| `Autoscale` | `ChartAutoscale` | as-is |
| `Overlay`, `Progress`, `Effect`, `EffectSpeed`, `ScrollSpeed` | same | as-is |
| `ProgressC`, `ProgressBC` | `ProgressColor`, `ProgressTrackColor` | `r,g,b` as-is |
| `EffectBlend` | `PaletteBlend` | as-is |

4. Diurnal: `Brightness=` unchanged; `GlobalTextColor=` → `TextColor=` with colour conversion.
5. A worked before/after example (the repo's previous `appsettings.json` Slack and TripTimer entries).
6. A closing checklist: no `Firmware` keys remain, every colour starts with `#` or is `r,g,b`, every
   duration is in ms, `TextColor: palette` accompanies every `Palette` that came from Rainbow/Gradient.

`docs/config-migration.md` carries the same tables and example for humans. README's "AWTRIX NG"
section is rewritten: every device is NG, `BaseTopic` semantics, the ValueMap vocabulary in NG
terms, a link to `docs/config-migration.md`. `CLAUDE.md`'s "Two Publisher Transports" paragraph is
rewritten to describe `AwtrixEndpoints` and drop the firmware sentence. `docs/simulator.md` loses its
`Firmware` mention.

## Deletions checklist

`Awtrix3Firmware.cs`, `AwtrixFirmware.cs`, `AwtrixFirmwareKind.cs`, `IAwtrixFirmware.cs`,
`NgFirmware.cs`, `NgPayloadTranslator.cs` (incl. `NgTranslation`, `NgPayloadKind`),
`NgSettingsTranslator.cs`, `NgColour.cs` (moved), `AwtrixAddress.Firmware` enum property,
`AwtrixRequest.DroppedKeys`, `AwtrixService._warnedDroppedKeys`, `AwtrixAppMessage.TryParseIntArray` /
`TryParseIntMatrix` if no longer used outside `ValueMapSetters` (move them there otherwise),
`ValueMapSetters` entries `TopText`, `Color`, `Gradient`, `BlinkText`, `FadeText`, `Background`,
`Rainbow`, `PushIcon`, `Duration`, `Line`, `Bar`, `Autoscale`, `ProgressC`, `ProgressBC`,
`EffectPalette`, `EffectBlend`, `Lifetime`, `LifetimeMode`, `TextOffset`, `Center`.
