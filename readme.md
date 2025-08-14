# AwtrixSharp

[![Docker](https://github.com/neutmute/awtrix-sharp/actions/workflows/docker-publish.yml/badge.svg)](https://github.com/neutmute/awtrix-sharp/actions/workflows/docker-publish.yml)

### Env Vars

| Variable                       | Example Value        | Description                |
|------------------------------- |---------------------|----------------------------|
| `AWTRIXSHARP_MQTT__HOST`       | `192.168.2.1`       | MQTT broker hostname/IP    |
| `AWTRIXSHARP_MQTT__USERNAME`   | `my  `              | MQTT username              |
| `AWTRIXSHARP_MQTT__PASSWORD`   | `pass`              | MQTT password              |
| `AWTRIXSHARP_SLACK__BOTTOKEN`  | `xoxb-`             | Slack bot token            |
| `AWTRIXSHARP_SLACK__USERID`    | `U123`             | Slack userId            |
| `AWTRIXSHARP_SLACK__APPTOKEN`    | `xapp-222`             | Slack app token            |

#### Powershell Script

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
