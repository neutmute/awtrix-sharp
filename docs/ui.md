# Test UI

The service hosts a small Blazor Server UI at `/ui` (same container as the API, no extra build). It is
open like Swagger: `ApiKeyMiddleware` skips `/ui`, `/_blazor` and `/_framework`. Unlike Swagger, whose
"Try it out" calls still require the API key when one is configured, `/ui` performs Notify, AppUpdate,
AppClear, Dismiss and Run now without any key — so configuring `Api:Key` does not protect the clocks
while `/ui` is reachable. Restrict network access to the container if that matters.

## Apps (`/ui`)

One card per device from `Awtrix:Devices`. For each configured app: its Config and ValueMaps, a **Run
now** button (calls `Conductor.ExecuteNow`, the same thing the `api/app/*/start` endpoints do), and the
last payload the app pushed (captured in memory by `PublishTrace`, 50 most recent sends). Per device:
**Dismiss notification** and **Clear app** (by name). The "Recent sends" list at the bottom shows
traffic from every app, including cron-fired ones.

## Visuals (`/ui/visuals`)

Compose an AWTRIX NG payload with the visual features from
https://blueforcer.github.io/awtrix-ng/reference/visuals/ : effect, effect speed, palette (name or
`r,g,b;r,g,b` stops), overlay, colours, icon, font, timing, scroll, and (for a custom app) transition.
The JSON on the right is what will be sent. Editing it switches to manual mode; **Back to form**
returns. Send as a notification or as a custom app update under a name you choose.

Dropdown lists come from the clock: `DeviceStateMonitor` subscribes to `{baseTopic}/state/capabilities`,
`{baseTopic}/state/settings` and `{baseTopic}/state/device` for every MQTT device. Until a capabilities
payload arrives (or for HTTP devices, which are not polled) the built-in lists in `NgVisuals` are used
and the page says so. The dropdowns list everything the clock reports; to try a value that is not in a list, edit the JSON panel directly.

## Local check

`dotnet run --project src/api --launch-profile Simulator` then open http://localhost:5115/ui. The
simulator device is HTTP, so it uses built-in lists.

The Simulator environment also enables static web assets (`builder.WebHost.UseStaticWebAssets()` in
`Program.cs`), so `_framework/blazor.web.js` is served straight from the build output and the page
becomes interactive without a `dotnet publish` first.
