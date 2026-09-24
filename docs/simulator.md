# Running against the AWTRIX NG simulator

The NG simulator runs the real firmware and web UI on your PC and serves HTTP API v1 on
`http://localhost:8080`. It is the only target AwtrixSharp may be run against locally.

## Build the simulator (once)

Needs PlatformIO (`pip install platformio`).

```bash
git clone https://github.com/Blueforcer/awtrix-ng
cd awtrix-ng
pio run -e native_sim
.pio\build\native_sim\program.exe      # Windows; .pio/build/native_sim/program on Linux/macOS
```

Open http://localhost:8080 for the live preview grid. `--port N` changes the port; `--no-matrix` silences the
terminal rendering.

## Run AwtrixSharp against it

```powershell
dotnet run --project src/api --launch-profile Simulator
```

The launch profile is required: a plain `dotnet run` uses the first profile in
`src/api/Properties/launchSettings.json`, which pins `ASPNETCORE_ENVIRONMENT=Development` and overrides your shell
(the alternative is `$env:ASPNETCORE_ENVIRONMENT = "Simulator"` with `dotnet run --project src/api --no-launch-profile`).

`appsettings.Simulator.json` is self-contained: it lists one NG device at `http://localhost:8080` (apps `DiurnalApp`
and `MqttClockRenderApp` only) and points MQTT at `localhost`. User secrets are only loaded in the Development
environment; in the Simulator environment `AWTRIXSHARP_`-prefixed environment variables are ignored and the base
`appsettings.json` is not loaded, so the real broker, clock and Slack workspace cannot be reached even if your
shell exports those variables. The Slack, Data and TransportOpenData literal environment-variable fallbacks
(`AWTRIXSHARP_SLACK__*`, `AWTRIXSHARP_SETTINGS__DATA_DIRECTORY`, `TRANSPORTOPENDATA__APIKEY`) are also disabled in
the Simulator environment, so a developer's real Slack app token can't leak into a Simulator run either.
`test/Test/Configuration/SimulatorEnvironmentTests.cs` pins all of this, including the `Simulator` launch profile.

## Optional: MQTT path

Run a local broker (`docker run -p 1883:1883 eclipse-mosquitto`), then in the simulator web UI (or
`PUT /api/v1/system`) set `mqttEnabled: true`, `mqttHost: <your PC's IP>`, `mqttPrefix: awtrix/sim`. Change the
device in `appsettings.Simulator.json` to `"BaseTopic": "awtrix/sim"` and publish a test value:
`mosquitto_pub -t awtrix-sim/temperature -m 21.5`.

## What to check

- Preview grid shows the pushed app; `GET http://localhost:8080/api/v1/apps` lists it.
- Diurnal: `GET /api/v1/settings` reflects `brightness`/`textColor` after the scheduled time.
- Any `422` is logged by `HttpPublisher` with the body naming the rejected field.
- Buttons: `POST /sim/...` endpoints (see the simulator docs) publish `state/buttons/*` over MQTT.
