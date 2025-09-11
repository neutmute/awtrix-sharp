# AwtrixSharp

[![Docker](https://github.com/neutmute/awtrix-sharp/actions/workflows/docker-publish.yml/badge.svg)](https://github.com/neutmute/awtrix-sharp/actions/workflows/docker-publish.yml)

A configurable dotnet controller for [Awtrix 3](https://blueforcer.github.io/awtrix3/#/) devices.

One instance of this app can control multiple clocks in your household.

Why?

1. The `TripTimerApp` and `SlackStatusApp` implementations needed more complex logic and unit tests than I wanted to write in OpenHAB DSL or Javascript
2. There are [Awtrix Flows](https://flows.blueforcer.de/) but I don't run Home Assistant
3. Easy [configuration](./src/api/appsettings.json) of multiple devices
4. The clock app pulses `:` every other second by sending a new MQTT payload. Doing this in OpenHAB would make too many logs

## What can it do

### `TripTimerApp`

![image](./docs/gifs/awtrix-train.png)

Uses [TransportNSW Trip Planner API](https://opendata.transport.nsw.gov.au/data/dataset/trip-planner-apis/resource/917c66c3-8123-4a0f-b1b1-b4220f32585d) on a schedule - or double press of the button - to start a countdown of how long you have until you have to get out of bed for each train over the next 30 minutes

Configure it with 

- a [Transport Open Data Hub](https://opendata.transport.nsw.gov.au/) API Key
- a `StopId`, found using [TripPlanner API](https://opendata.transport.nsw.gov.au/dataset/trip-planner-apis) for departure and destination stops
- Your travel time to the station
- Your preparation time (eg: get dressed, breakfast, pack bag)

#### TripTimerApp AppSettings Configuration Example

``` 
{
   "Type": "TripTimerApp",
   "Config": {
     "CronSchedule": "10 6 * * 1-5",
     "ActiveTime": "01:00:00",
     "StopIdOrigin": "200060",
     "StopIdDestination": "200070",
     "TimeToOrigin": "00:14:00",
     "TimeToPrepare": "00:08:00"
   },
   "ValueMaps": [
     {
       "ValueMatcher": "",
       "Icon": "1667",
       "Text": "Go now!",
       "Color": "#FFFFFF"
     }
   ]
}
 ```

### `DiurnalApp`

On a schedule, set the brightness and color of the display. 

![image](./docs/gifs/awtrix-mqttclockrender.gif)

For example, dim down to red at night. Brighten up to white during the day

#### DiurnalApp AppSettings Configuration Example

```
 {
   "Type": "DiurnalApp",
   "Config": {
     "0600": "Brightness=8",
     "0700": "GlobalTextColor=#FFFFFF",
     "1900": "GlobalTextColor=#FF0000",
     "2100": "Brightness=1"
   }
 }
```

### `MqttRenderApp`

![image](./docs/gifs/awtrix-mqttrender.gif)

Subscribe to an MQTT topic and render out the value.
Using the `ValueMap` section, mutate the display configuration to show red text and icon on a negative value, yellow on a positive (say)

```
 {
   "Type": "MqttRenderApp",
   "Config": {
     "CronSchedule": "0 8 * * *",
     "ActiveTime": "09:00:00",
     "ReadTopic": "openhab/fronius/grid-surplus"
   },
   "ValueMaps": [
     {
       "ValueMatcher": "^-",
       "Icon": "52465",
       "Color": "#FF0000"
     },
     {
       "ValueMatcher": "^(?!-).*",
       "Icon": "52464",
       "Color": "#FFDE21"
     }
   ]
 }
```

### `MqttClockRenderApp`

Render the time AND an MQTT value (eg: temperature) without having to swap between apps

![image](./docs/gifs/awtrix-mqttclockrender-dirunal.gif)

```
{
  "Type": "MqttClockRenderApp",
  "Config": {
    "CronSchedule": "0 0 * * *",
    "ActiveTime": "23:59:00",
    "ReadTopic": "openhab/temperature/room-j"
  }
}
```

### `SlackStatusApp`

When you change your status in Slack, render it to the Awtrix 3. 
Put the clock on your desk, set yourself to `Busy` and let even passer's by know

![image](./docs/gifs/awtrix-slack.png)

```
{
  "Type": "SlackStatusApp",
  "Config": {
    "SlackUserId": "U123"
  },
  "ValueMaps": [
    {
      "ValueMatcher": "busy",
      "Icon": "38789",
      "Text": "Busy",
      "Color": "#FF0000",
      "Background": "#FFFFFF",
      "Duration": "60"
    },
    {
      "ValueMatcher": "lunch",
      "Icon": "21380",
      "Text": "lunch",
      "Color": "#FFFFFF",
      "Background": "#000000",
      "Duration": "60"
    }
  ]
}
```

## Environment Variables

| Variable                       | Example Value        | Description                |
|------------------------------- |---------------------|----------------------------|
| `AWTRIXSHARP_MQTT__HOST`       | `192.168.10.10`       | MQTT broker hostname/IP    |
| `AWTRIXSHARP_MQTT__USERNAME`   | `my  `              | MQTT username              |
| `AWTRIXSHARP_MQTT__PASSWORD`   | `pass`              | MQTT password              |
| `AWTRIXSHARP_SLACK__USERID`    | `U123`             | Slack userId - user to monitor for status changes           |
| `AWTRIXSHARP_SLACK__APPTOKEN`    | `xapp-222`             | Slack app token            |
| `AWTRIXSHARP_SLACK__BOTTOKEN`  | `xoxb-`             | Slack bot token - reserved for future use           |
| `TRANSPORTOPENDATA__APIKEY` | `your-api-key`     | NSW Transport Trip Planner API key |

## Installation

1. Create your `docker-compose.yaml`
2. Create `./data/awtrix/appsettings.json`
3. Set the `basetopic` to be either an mqtt route - eg `awtrix/clock1` or a http url like `http://192.168.10.20/api`.

### Example docker compose

```yaml
services:
  mosquitto:
    image: eclipse-mosquitto:latest
    container_name: mosquitto
    user: "1000:1000"
    restart: always
    volumes:
      - ./data/mosquitto/config:/mosquitto/config
      - ./data/mosquitto/data:/mosquitto/data
      - ./data/mosquitto/log:/mosquitto/log
    ports:
      - 1883:1883
      - 8883:8883
    environment:
      TZ: "Australia/Sydney"

  awtrix:
    container_name: awtrix
    image: ghcr.io/neutmute/awtrix-sharp:master
    restart: always
    ports:
      - 80:8080
    volumes:
      - ./data/awtrix/appsettings.json:/app-api/appsettings.json
    environment:
      TZ: "Australia/Sydney"
      ASPNETCORE_ENVIRONMENT: "Production"
      AWTRIXSHARP_MQTT__HOST: "mosquitto"
      AWTRIXSHARP_MQTT__USERNAME: "xxxxxxxxxxxxxxx"
      AWTRIXSHARP_MQTT__PASSWORD: "xxxxxxxxxxxxxxx"
      AWTRIXSHARP_SLACK__APPTOKEN: "xapp-1-xxxxx"       # Optional, only required for SlackStatusApp
      TRANSPORTOPENDATA__APIKEY: "your-api-key-here"    # Optional, only required for TripTimerApp
```

## Development

A Swagger API supports testing and development

### Powershell Script

Configure your env vars with appropriate secrets for development

```
$mqttHostname = ""
$mqttUsername = ""
$mqttPassword = ""
$slackBotToken = ""
$slackUserId = ""
$slackAppToken = ""
$tripPlannerApiKey = ""
[System.Environment]::SetEnvironmentVariable('AWTRIXSHARP_MQTT__HOST'       , $mqttHostname     , [System.EnvironmentVariableTarget]::Machine)
[System.Environment]::SetEnvironmentVariable('AWTRIXSHARP_MQTT__USERNAME'   , $mqttUsername     , [System.EnvironmentVariableTarget]::Machine)
[System.Environment]::SetEnvironmentVariable('AWTRIXSHARP_MQTT__PASSWORD'   , $mqttPassword     , [System.EnvironmentVariableTarget]::Machine)
[System.Environment]::SetEnvironmentVariable('AWTRIXSHARP_SLACK__BOTTOKEN'  , $slackBotToken    , [System.EnvironmentVariableTarget]::Machine)
[System.Environment]::SetEnvironmentVariable('AWTRIXSHARP_SLACK__USERID'    , $slackUserId      , [System.EnvironmentVariableTarget]::Machine)
[System.Environment]::SetEnvironmentVariable('AWTRIXSHARP_SLACK__APPTOKEN'  , $slackAppToken    , [System.EnvironmentVariableTarget]::Machine)
[System.Environment]::SetEnvironmentVariable('TRANSPORTOPENDATA__APIKEY'    , $tripPlannerApiKey, [System.EnvironmentVariableTarget]::Machine)
```