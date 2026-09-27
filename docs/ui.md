# Test UI

The service hosts a small Blazor Server UI at `/` (also `/ui`) in the same container as the API, with no
extra build. It is open like Swagger: `ApiKeyMiddleware` skips `/ui`, `/_blazor` and `/_framework`. Unlike Swagger, whose
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
The JSON on the right is what will be sent, and it is two-way: editing the JSON updates the form
fields, and keys the form has no field for are kept and sent as typed. Invalid JSON only disables Send. Send as a notification or as a custom app update under a name you choose.

Dropdown lists come from the clock: `DeviceStateMonitor` subscribes to `{baseTopic}/state/capabilities`,
`{baseTopic}/state/settings` and `{baseTopic}/state/device` for every MQTT device. Until a capabilities
payload arrives (or for HTTP devices, which are not polled) the built-in lists in `NgVisuals` are used
and the page says so. The dropdowns list everything the clock reports; to try a value that is not in a list, edit the JSON panel directly.

## Persisting data-protection keys

Blazor's circuit and antiforgery tokens are protected with the ASP.NET Core data-protection key ring,
which is ephemeral inside a container. After a restart, a browser still holding the old cookie logs one
`AntiforgeryValidationException ... key was not found in the key ring` on its first request; it is
harmless and clears itself. To avoid it, persist the keys in a mounted folder that the container user
(uid 1654 in the official image) can write:

```
AWTRIXSHARP_DATAPROTECTION__KEYSPATH=/keys
docker run ... -e AWTRIXSHARP_DATAPROTECTION__KEYSPATH=/keys -v /srv/awtrix/keys:/keys ...
```

If the folder cannot be created or written the service logs a startup warning and keeps the default.

## Local check

`dotnet run --project src/api --launch-profile Simulator` then open http://localhost:5115/ui. The
simulator device is HTTP, so it uses built-in lists.

The Simulator environment also enables static web assets (`builder.WebHost.UseStaticWebAssets()` in
`Program.cs`), so `_framework/blazor.web.js` is served straight from the build output and the page
becomes interactive without a `dotnet publish` first.
