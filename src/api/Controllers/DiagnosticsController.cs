using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.Annotations;
using System.Text.Json;

namespace AwtrixSharpWeb.Controllers
{
    [SwaggerTag("Diagnostics")]
    [ApiController]
    [Route("[controller]")]
    public class DiagnosticsController : ControllerBase
    {
        private readonly ILogger<DiagnosticsController> _logger;
        private readonly AwtrixConfig _awtrixConfig;
        private readonly MqttConnector _mqttService;
        private readonly AwtrixService _awtrixService;
        private readonly Conductor _conductor;
        private readonly JsonSerializerOptions _jsonOptions;

        public DiagnosticsController(
            ILogger<DiagnosticsController> logger
            , IOptions<AwtrixConfig> devices
            , MqttConnector mqttService
            , AwtrixService awtrixService
            , Conductor conductor
            , JsonSerializerOptions jsonOptions
            )
        {
            _awtrixConfig = devices.Value;
            _logger = logger;
            _mqttService = mqttService;
            _awtrixService = awtrixService;
            _conductor = conductor;
            _jsonOptions = jsonOptions;
        }

        [HttpGet("")]
        public IActionResult Get()
        {
            var diagnosticInfo = new
            {
                Timestamp = DateTime.UtcNow,
                Message = "Hello, world"
            };

            var payload = System.Text.Json.JsonSerializer.Serialize(diagnosticInfo);

            return Ok(diagnosticInfo);
        }

        //[HttpGet("config")]
        //[SwaggerOperation(Summary = "Get configuration information", Description = "Returns information about the loaded configuration including ValueMaps")]
        //public IActionResult GetConfig()
        //{
        //    var configInfo = new
        //    {
        //        Timestamp = DateTime.UtcNow,
        //        Devices = _awtrixConfig.Devices.Select(device => new
        //        {
        //            BaseTopic = device.BaseTopic,
        //            Apps = device.Apps?.Select(app => new 
        //            {
        //                Name = app.Name,
        //                Type = app.Type,
        //                Environment = app.Environment,
        //                ConfigEntries = app.Config?.ToDictionary(k => k.Key, k => k.Value),
        //                ConfigCount = app.Config?.Count ?? 0,
        //                ValueMapsCount = app.ValueMaps?.Count ?? 0,
        //                ValueMaps = app.ValueMaps?.Select(vm => new
        //                {
        //                    ValueMatcher = vm.ValueMatcher,
        //                    Properties = vm.Keys
        //                        .Where(k => k != "ValueMatcher")
        //                        .ToDictionary(k => k, k => vm[k])
        //                }).ToList()
        //            }).ToList()
        //        }).ToList()
        //    };

        //    return Ok(configInfo);
        //}

        //[HttpGet("config/json")]
        //[SwaggerOperation(Summary = "Get configuration as JSON", Description = "Returns the raw configuration serialized with the custom JSON converters")]
        //public IActionResult GetConfigAsJson()
        //{
        //    try
        //    {
        //        var json = JsonSerializer.Serialize(_awtrixConfig, _jsonOptions);
        //        return Content(json, "application/json");
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Error serializing config to JSON");
        //        return StatusCode(500, $"Error: {ex.Message}");
        //    }
        //}

        //[HttpPost("config/test")]
        //[SwaggerOperation(Summary = "Test JSON converter", Description = "Tests the AppConfigJsonConverter by serializing and deserializing an AppConfig")]
        //public IActionResult TestJsonConverter([FromBody] AppConfig appConfig)
        //{
        //    try
        //    {
        //        // Serialize the input to JSON
        //        var json = JsonSerializer.Serialize(appConfig, _jsonOptions);
                
        //        // Deserialize back to AppConfig
        //        var deserializedConfig = JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions);
                
        //        // Return both for comparison
        //        return Ok(new
        //        {
        //            Original = appConfig,
        //            Json = json,
        //            Deserialized = deserializedConfig
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Error testing JSON converter");
        //        return StatusCode(500, $"Error: {ex.Message}");
        //    }
        //}

        //[HttpPost("config/testkeys")]
        //[SwaggerOperation(Summary = "Test AppConfigKeys JSON converter", Description = "Tests the AppConfigKeysJsonConverter by serializing and deserializing AppConfigKeys")]
        //public IActionResult TestConfigKeysConverter([FromBody] Dictionary<string, object> values)
        //{
        //    try
        //    {
        //        // Create an AppConfigKeys instance
        //        var configKeys = new AppConfigKeys();
                
        //        // Add the values
        //        foreach (var kvp in values)
        //        {
        //            configKeys[kvp.Key] = kvp.Value?.ToString();
        //        }
                
        //        // Serialize to JSON
        //        var json = JsonSerializer.Serialize(configKeys, _jsonOptions);
                
        //        // Deserialize back
        //        var deserializedKeys = JsonSerializer.Deserialize<AppConfigKeys>(json, _jsonOptions);
                
        //        return Ok(new
        //        {
        //            Original = configKeys,
        //            Json = json,
        //            Deserialized = deserializedKeys
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Error testing AppConfigKeys converter");
        //        return StatusCode(500, $"Error: {ex.Message}");
        //    }
        //}

        //[HttpPost("diurnal/test")]
        //[SwaggerOperation(Summary = "Test DiurnalApp configuration", Description = "Triggers DiurnalApp to execute with the current time")]
        //public IActionResult TestDiurnalApp(string time = null)
        //{
        //    try
        //    {
        //        var apps = _conductor.FindApps("DiurnalApp");
        //        if (apps == null || apps.Count == 0)
        //        {
        //            return NotFound("DiurnalApp not found");
        //        }

        //        var timeToTest = string.IsNullOrEmpty(time) 
        //            ? DateTime.Now 
        //            : DateTime.Parse(time);

        //        _logger.LogInformation("Testing DiurnalApp with time: {Time}", timeToTest);

        //        foreach (var app in apps)
        //        {
        //            var config = app.GetConfig();
        //            _logger.LogInformation("DiurnalApp config: {Config}", config);
                    
        //            // Log the config entries
        //            if (config is AppConfig appConfig)
        //            {
        //                _logger.LogInformation("DiurnalApp Config entries: {Count}", appConfig.Config?.Count ?? 0);
        //                if (appConfig.Config != null)
        //                {
        //                    foreach (var entry in appConfig.Config)
        //                    {
        //                        _logger.LogInformation("  {Key} = {Value}", entry.Key, entry.Value);
        //                    }
        //                }
        //            }
        //        }

        //        return Ok(new { 
        //            Message = "DiurnalApp configuration logged", 
        //            Time = timeToTest,
        //            AppCount = apps.Count
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Error testing DiurnalApp");
        //        return StatusCode(500, $"Error: {ex.Message}");
        //    }
        //}

        [HttpPost("mqtt")]
        public async Task<IActionResult> Mqtt()
        {
            var diagnosticInfo = new
            {
                Timestamp = DateTime.UtcNow,
                Service = "AwtrixSharp",
                Status = "Running",
                Message = "Diagnostic test"
            };
            
            var payload = System.Text.Json.JsonSerializer.Serialize(diagnosticInfo);
            
            await _mqttService.PublishAsync("awtrixsharp/diagnostic", payload);
            
            return Ok(diagnosticInfo);
        }

        [HttpPost("awtrix/text")]
        public async Task<IActionResult> AwtrixText(string text = "Awtrix Sharp!")
        {
            foreach(var device in _awtrixConfig.Devices)
            {
                if (string.IsNullOrEmpty(device.BaseTopic))
                {
                    _logger.LogWarning("Device {Device} has an empty BaseTopic", device);
                    continue;
                }

                var payload = new AwtrixAppMessage()
                                    .SetText(text)
                                    .SetProgress(50)
                                    .SetStack(false)
                                    .SetDuration(TimeSpan.FromSeconds(5));

                await _awtrixService.Dismiss(device);
                await _awtrixService.Notify(device, payload);
            }

            return Ok();
        }

        [HttpPost("awtrix/progress")]
        public async Task<IActionResult> AwtrixProgess(int progress)
        {
            foreach (var device in _awtrixConfig.Devices)
            {
                if (string.IsNullOrEmpty(device.BaseTopic))
                {
                    _logger.LogWarning("Device {Device} has an empty BaseTopic", device);
                    continue;
                }

                var payload = new AwtrixAppMessage()
                                    .SetText(progress.ToString())
                                    .SetProgress(progress)
                                    .SetDuration(TimeSpan.FromSeconds(1));

                await _awtrixService.Notify(device, payload);
            }

            return Ok();
        }

        [HttpPost("awtrix/rtttl")]
        public async Task<IActionResult> AwtrixRtttl(string rtttl)
        {
            foreach (var device in _awtrixConfig.Devices)
            {
                if (string.IsNullOrEmpty(device.BaseTopic))
                {
                    _logger.LogWarning("Device {Device} has an empty BaseTopic", device);
                    continue;
                }

                await _awtrixService.PlayRtttl(device, rtttl);
            }

            return Ok();
        }

        [HttpPost("awtrix/settings/text-color")]
        public async Task<IActionResult> SetGlobalTextColor(string hexColor = "#00FF00")
        {
            foreach (var device in _awtrixConfig.Devices)
            {
                var message = new AwtrixSettings().SetGlobalTextColor(hexColor);
                await _awtrixService.Set(device, message);
            }

            return Ok();
        }
    }
}
