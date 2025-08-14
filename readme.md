# AwtrixSharp

[![Docker](https://github.com/neutmute/awtrix-sharp/actions/workflows/docker-publish.yml/badge.svg)](https://github.com/neutmute/awtrix-sharp/actions/workflows/docker-publish.yml)

## Env Vars

| Variable                       | Example Value        | Description                |
|------------------------------- |---------------------|----------------------------|
| `AWTRIXSHARP_MQTT__HOST`       | `192.168.10.10`       | MQTT broker hostname/IP    |
| `AWTRIXSHARP_MQTT__USERNAME`   | `my  `              | MQTT username              |
| `AWTRIXSHARP_MQTT__PASSWORD`   | `pass`              | MQTT password              |
| `AWTRIXSHARP_SLACK__USERID`    | `U123`             | Slack userId - user to monitor for status changes           |
| `AWTRIXSHARP_SLACK__APPTOKEN`    | `xapp-222`             | Slack app token            |
| `AWTRIXSHARP_SLACK__BOTTOKEN`  | `xoxb-`             | Slack bot token - reserved for future use           |

### Powershell Script

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
    ports:
      - 80:8080
    volumes:
      - ./data/awtrix/appsettings.json:/app/appsettings.json
    environment:
      TZ: "Australia/Sydney"
      AWTRIXSHARP_MQTT__HOST: "mosquitto"
      AWTRIXSHARP_MQTT__USERNAME: "xxxxxxxxxxxxxxx"
      AWTRIXSHARP_MQTT__PASSWORD: "xxxxxxxxxxxxxxx"
      AWTRIXSHARP_SLACK__APPTOKEN: "xapp-1-xxxxx"
      AWTRIXSHARP_SLACK__USERID: "Uxxxxxx"
```

## Development

Configure your env vars with appropriate secrets

```
$mqttHostname = ""
$mqttUsername = ""
$mqttPassword = ""
$slackBotToken = ""
$slackUserId = ""
$slackAppToken = ""
[System.Environment]::SetEnvironmentVariable('AWTRIXSHARP_MQTT__HOST', $mqttHostname, [System.EnvironmentVariableTarget]::Machine)
[System.Environment]::SetEnvironmentVariable('AWTRIXSHARP_MQTT__USERNAME', $mqttUsername, [System.EnvironmentVariableTarget]::Machine)
[System.Environment]::SetEnvironmentVariable('AWTRIXSHARP_MQTT__PASSWORD', $mqttPassword, [System.EnvironmentVariableTarget]::Machine)
[System.Environment]::SetEnvironmentVariable('AWTRIXSHARP_SLACK__BOTTOKEN', $slackBotToken, [System.EnvironmentVariableTarget]::Machine)
[System.Environment]::SetEnvironmentVariable('AWTRIXSHARP_SLACK__USERID', $slackUserId, [System.EnvironmentVariableTarget]::Machine)
[System.Environment]::SetEnvironmentVariable('AWTRIXSHARP_SLACK__APPTOKEN', $slackAppToken, [System.EnvironmentVariableTarget]::Machine)
```