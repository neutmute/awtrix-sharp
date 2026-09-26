---
name: awtrix3-config-migration
description: Use when asked to convert, migrate or upgrade an AwtrixSharp appsettings.json (or a device/app/ValueMap fragment) from AWTRIX 3 vocabulary to AWTRIX NG - triggers on "convert this config", "migrate to NG", "old appsettings", "awtrix3 config", or a pasted config containing Color/Duration/Rainbow/PushIcon/GlobalTextColor/Firmware keys.
---

# AWTRIX 3 → AWTRIX NG config migration

The service speaks NG natively (see `docs/config-migration.md`, which is the human copy of these rules).
Convert configs deterministically; never invent values; report everything you drop.

## Procedure

1. Read the pasted JSON or the referenced file. Do not modify the file unless asked; output the converted JSON.
2. Apply the device, colour, ValueMap and Diurnal rules below to every device and app.
3. Output the full converted JSON, then a bullet list: what was renamed, what was converted, what was
   dropped and why. Anything in the "no equivalent" list must be named explicitly.
4. Run the checklist at the end and state that each item holds.

## Device rules

- Remove `"Firmware"` (any value).
- HTTP `BaseTopic`: strip a trailing `/api/v1` or `/api`, then any trailing `/`. Example: `http://192.168.1.50/api` → `http://192.168.1.50`.
- MQTT `BaseTopic`: unchanged.

## Colour rule

Accepted NG forms: `#RRGGBB`, `#RGB`, `r,g,b`. Bare `RRGGBB` → prefix `#`. Any other value: drop and report.

## ValueMap key table

| AWTRIX 3 | NG | Value conversion |
|---|---|---|
| `Text` (string) | `Text` | as-is |
| `Text` (`[{"t","c"}]` JSON) | none | drop and report (only TripTimerApp produced these; it builds them in code now) |
| `TextCase` | `TextCase` | `0`→`inherit`, `1`→`upper`, `2`→`asTyped`; other → drop and report |
| `TopText` | none | drop and report |
| `Hold`, `Stack` | same | as-is |
| `TextOffset` | `TextOffsetX` | as-is |
| `Center` | `TextCenter` | as-is |
| `Color` | `TextColor` | colour rule |
| `Background` | `BackgroundColor` | colour rule |
| `Gradient` (`r,g,b;r,g,b`) | `Palette` + `TextColor: "palette"` | matrix as-is |
| `Rainbow` | `Palette: "Rainbow"` + `TextColor: "palette"` | only when `true`; `false` → drop silently |
| `EffectPalette` | `Palette` | as-is |
| `BlinkText`, `FadeText` | `TextBlinkMs`, `TextFadeMs` | round to integer |
| `Icon` | `Icon` | as-is |
| `PushIcon` | `IconMode` | `0`→`fixed`, `1`→`pushOnce`, `2`→`push`; other → drop and report |
| `Duration` | `DurationMs` | seconds × 1000 |
| `Lifetime` | `LifetimeMs` | seconds × 1000 |
| `LifetimeMode` | `LifetimeExpiry` | `0`→`remove`, `1`→`mark`; other → drop and report |
| `Line`, `Bar` | `LineChart`, `BarChart` | as-is |
| `Autoscale` | `ChartAutoscale` | as-is |
| `Overlay`, `Progress`, `Effect`, `EffectSpeed`, `ScrollSpeed` | same | as-is |
| `ProgressC`, `ProgressBC` | `ProgressColor`, `ProgressTrackColor` | as-is (`r,g,b`) |
| `EffectBlend` | `PaletteBlend` | as-is |
| `ValueMatcher` | `ValueMatcher` | as-is |
| anything else | none | drop and report |

Palette precedence when several sources are present in one map: `Gradient` > `Rainbow` > `EffectPalette`.
When the winner is `Gradient` or `Rainbow`, set `"TextColor": "palette"` and drop any `Color` (report it).
When the winner is `EffectPalette`, keep `Color` → `TextColor` as normal.

Keys are case-insensitive on input; emit them in the PascalCase spelling shown above.

## DiurnalApp rules

Config values are `Name=Value[;Name=Value]` strings keyed by `HHmm`:
- `Brightness=` unchanged.
- `GlobalTextColor=` → `TextColor=` with the colour rule.
- Anything else: leave as-is and report (the service warns on unknown settings).

## Worked example

Input:

```json
{ "ValueMatcher": "", "Icon": "1667", "Text": "Go now!", "Color": "FFFFFF", "Rainbow": "true", "Duration": "60", "PushIcon": "1" }
```

Output:

```json
{ "ValueMatcher": "", "Icon": "1667", "Text": "Go now!", "Palette": "Rainbow", "TextColor": "palette", "DurationMs": "60000", "IconMode": "pushOnce" }
```

Report: `Rainbow` → `Palette: Rainbow` + `TextColor: palette`; `Color: FFFFFF` dropped because the palette
colours the text; `Duration` 60 s → `DurationMs` 60000; `PushIcon` 1 → `IconMode: pushOnce`.

## Checklist (state each explicitly at the end)

- No `Firmware` keys remain.
- Every colour starts with `#` or is `r,g,b`.
- Every `DurationMs` / `LifetimeMs` is in milliseconds.
- Every `Palette` from `Rainbow`/`Gradient` has `"TextColor": "palette"` beside it.
- HTTP `BaseTopic` values have no `/api` suffix and no trailing slash.
- Every dropped key was reported.
