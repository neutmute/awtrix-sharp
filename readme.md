# AwtrixSharp

### Env Vars

| Variable                       | Example Value        | Description                |
|------------------------------- |---------------------|----------------------------|
| `AWTRIXSHARP_MQTT__HOST`       | `192.168.2.1`       | MQTT broker hostname/IP    |
| `AWTRIXSHARP_MQTT__USERNAME`   | `my  `              | MQTT username              |
| `AWTRIXSHARP_MQTT__PASSWORD`   | `pass`              | MQTT password              |

#### Powershell Script

```
$mqttHostname = ""
$mqttUsername = ""
$mqttPassword = ""
[System.Environment]::SetEnvironmentVariable('AWTRIXSHARP_MQTT__HOST', $mqttHostname, [System.EnvironmentVariableTarget]::Machine)
[System.Environment]::SetEnvironmentVariable('AWTRIXSHARP_MQTT__USERNAME', $mqttUsername, [System.EnvironmentVariableTarget]::Machine)
[System.Environment]::SetEnvironmentVariable('AWTRIXSHARP_MQTT__PASSWORD', $mqttPassword, [System.EnvironmentVariableTarget]::Machine)
```