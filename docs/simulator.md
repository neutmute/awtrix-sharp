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
$env:ASPNETCORE_ENVIRONMENT = "Simulator"
dotnet run --project src/api
```

`appsettings.Simulator.json` lists one NG device at `http://localhost:8080` and points MQTT at `localhost`.
User secrets are only loaded in the Development environment, so the real broker and clock are unreachable
(`test/Test/Configuration/SimulatorEnvironmentTests.cs` pins this).

**Warning:** `AWTRIXSHARP_`-prefixed environment variables override every other configuration source,
including `appsettings.Simulator.json` (see the precedence note in `Program.SetupConfiguration`). If your
shell profile persistently exports real broker settings (e.g. `AWTRIXSHARP_MQTT__HOST`,
`AWTRIXSHARP_MQTT__USERNAME`, `AWTRIXSHARP_MQTT__PASSWORD`), those values leak into the Simulator
environment too and `MqttClockRenderApp` (or any MQTT-reading app) will connect to the real broker with
real credentials. Unset any `AWTRIXSHARP_MQTT__*` variables in the shell before running in Simulator mode,
or confirm none are set with `env | grep AWTRIXSHARP_` (`Get-ChildItem Env: | Where-Object Name -like
'AWTRIXSHARP_*'` on PowerShell).

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
