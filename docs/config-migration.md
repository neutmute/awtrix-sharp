# Migrating an AWTRIX 3 configuration to AWTRIX NG

AwtrixSharp speaks AWTRIX NG natively. `appsettings.json` uses NG payload names and NG value formats;
nothing is translated at runtime. Configs written for AWTRIX 3 must be converted once. Unknown ValueMap
keys are reported at startup with a pointer here and ignored.

Tip: in Claude Code, paste the old config and ask for it to be migrated; the `awtrix3-config-migration`
project skill applies these rules.

## Device

| AWTRIX 3 | NG |
|---|---|
| `"Firmware": "NG"` / `"Awtrix3"` | remove the key (a leftover key is warned about at startup) |
| MQTT `BaseTopic: "awtrix/clock1"` | unchanged: the device's `mqttPrefix` |
| HTTP `BaseTopic: "http://192.168.1.50/api"` | `"http://192.168.1.50"` (no `/api` or `/api/v1`; no trailing `/`) |

## Colours

NG colours are `#RRGGBB`, `#RGB` or `r,g,b`. Bare `RRGGBB` is invalid: add the `#`.

## ValueMap keys

| AWTRIX 3 | NG | Value conversion |
|---|---|---|
| `Text` (string) | `Text` | as-is |
| `Text` (`[{"t","c"}]` JSON fragments) | not expressible in a ValueMap | drop; only `TripTimerApp` produced these and it now builds them in code |
| `TextCase` 0/1/2 | `TextCase` | `inherit` / `upper` / `asTyped` |
| `TopText` | — | drop (no NG equivalent) |
| `Hold`, `Stack` | same | as-is |
| `TextOffset` | `TextOffsetX` | as-is |
| `Center` | `TextCenter` | as-is |
| `Color` | `TextColor` | colour |
| `Background` | `BackgroundColor` | colour |
| `Gradient` `r,g,b;r,g,b` | `Palette` + `TextColor: "palette"` | matrix as-is |
| `Rainbow: true` | `Palette: "Rainbow"` + `TextColor: "palette"` | `Rainbow: false` → drop |
| `EffectPalette` | `Palette` | as-is; `Gradient` and `Rainbow` win over it when several are present |
| `BlinkText`, `FadeText` | `TextBlinkMs`, `TextFadeMs` | round to a whole number |
| `Icon` | `Icon` | as-is |
| `PushIcon` 0/1/2 | `IconMode` | `fixed` / `pushOnce` / `push` |
| `Duration` (seconds) | `DurationMs` | × 1000 |
| `Lifetime` (seconds) | `LifetimeMs` | × 1000 |
| `LifetimeMode` 0/1 | `LifetimeExpiry` | `remove` / `mark` |
| `Line`, `Bar` | `LineChart`, `BarChart` | as-is |
| `Autoscale` | `ChartAutoscale` | as-is |
| `Overlay`, `Progress`, `Effect`, `EffectSpeed`, `ScrollSpeed` | same | as-is |
| none | `ScrollMode` | NG-only: `static`, `wrap`, `loop` or `bounce`; merges with `ScrollSpeed` into the `scroll` object |
| `ProgressC`, `ProgressBC` | `ProgressColor`, `ProgressTrackColor` | `r,g,b` as-is |
| `EffectBlend` | `PaletteBlend` | as-is |
| `ValueMatcher` | `ValueMatcher` | as-is |
| anything else | none | drop and report |

Palette precedence when several sources are present in one map: `Gradient` > `Rainbow` > `EffectPalette`.

A `Palette` that came from `Rainbow` or `Gradient` must be accompanied by `"TextColor": "palette"`,
otherwise NG keeps the text in its plain colour.

## DiurnalApp

| AWTRIX 3 | NG |
|---|---|
| `Brightness=8` | `Brightness=8` |
| `GlobalTextColor=#FFFFFF` | `TextColor=#FFFFFF` |

## Example

Before:

```json
{
  "BaseTopic": "awtrix/clock1",
  "Firmware": "NG",
  "Apps": [
    { "Type": "DiurnalApp", "Config": { "0700": "GlobalTextColor=#FFFFFF", "2100": "Brightness=1" } },
    {
      "Type": "SlackStatusApp",
      "Config": { "SlackUserId": "" },
      "ValueMaps": [
        { "ValueMatcher": "busy", "Icon": "38789", "Text": "Busy", "Color": "#FF0000", "Background": "#FFFFFF", "Duration": "60" }
      ]
    },
    {
      "Type": "TripTimerApp",
      "Config": { "CronSchedule": "10 6 * * 1-5", "ActiveTime": "01:00:00", "StopIdOrigin": "200060", "StopIdDestination": "200070" },
      "ValueMaps": [ { "ValueMatcher": "", "Icon": "1667", "Text": "Go now!", "Color": "FFFFFF", "Rainbow": "true" } ]
    }
  ]
}
```

After:

```json
{
  "BaseTopic": "awtrix/clock1",
  "Apps": [
    { "Type": "DiurnalApp", "Config": { "0700": "TextColor=#FFFFFF", "2100": "Brightness=1" } },
    {
      "Type": "SlackStatusApp",
      "Config": { "SlackUserId": "" },
      "ValueMaps": [
        { "ValueMatcher": "busy", "Icon": "38789", "Text": "Busy", "TextColor": "#FF0000", "BackgroundColor": "#FFFFFF", "DurationMs": "60000" }
      ]
    },
    {
      "Type": "TripTimerApp",
      "Config": { "CronSchedule": "10 6 * * 1-5", "ActiveTime": "01:00:00", "StopIdOrigin": "200060", "StopIdDestination": "200070" },
      "ValueMaps": [ { "ValueMatcher": "", "Icon": "1667", "Text": "Go now!", "Palette": "Rainbow", "TextColor": "palette" } ]
    }
  ]
}
```

Note the TripTimer map: `Rainbow` beat `Color`, so `TextColor` became `palette` and the white was dropped.

## Checklist

- No `Firmware` keys remain.
- Every colour starts with `#` or is `r,g,b`.
- Every duration and lifetime is in milliseconds.
- Every `Palette` that came from `Rainbow` or `Gradient` has `"TextColor": "palette"` beside it.
- HTTP `BaseTopic` values have no `/api` suffix and no trailing slash.
