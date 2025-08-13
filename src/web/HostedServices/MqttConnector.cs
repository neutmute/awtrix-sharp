using MQTTnet;
using MQTTnet.Formatter;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AwtrixSharpWeb.Domain;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AwtrixSharpWeb.HostedServices
{
    public class MqttConnector : IHostedService
    {
        private IMqttClient _client;
        private readonly ILogger<MqttConnector> _log;
        private readonly MqttSettings _settings;

        public MqttConnector(ILogger<MqttConnector> logger, IOptions<MqttSettings> settings)
        {
            _log = logger;
            _settings = settings.Value;
        }

        public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            _log.LogInformation("Connecting to MQTT broker at {Host}...", _settings.Host);
            var mqttClientFactory = new MqttClientFactory();

            var clientOptionsBuilder = mqttClientFactory.CreateClientOptionsBuilder()
                .WithTcpServer(_settings.Host)
                .WithProtocolVersion(MqttProtocolVersion.V500);

            // Add credentials if provided
            if (!string.IsNullOrEmpty(_settings.Username))
            {
                clientOptionsBuilder.WithCredentials(_settings.Username, _settings.Password);
                _log.LogDebug("Using credentials for MQTT connection");
            }

            var clientOptions = clientOptionsBuilder.Build();

            _client = mqttClientFactory.CreateMqttClient();

            try
            {
                // Use a timeout for the connection attempt
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    timeoutCts.Token, 
                    cancellationToken == default ? CancellationToken.None : cancellationToken);

                var result = await _client.ConnectAsync(clientOptions, linkedCts.Token);
                
                if (_client.IsConnected)
                {
                    _log.LogInformation("Connected to MQTT broker");
                    return true;
                }
                else
                {
                    _log.LogError($"Failed to connect to MQTT broker: {result.ReasonString}");
                    return false;
                }
            }
            catch (OperationCanceledException ex)
            {
                _log.LogError(ex, "Connection to MQTT broker timed out");
                return false;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to connect to MQTT broker");
                return false;
            }
        }

        public async Task PublishAsync(string topic, string payload)
        {
            _log.LogInformation("Publishing MQTT to topic {Topic} with payload {Payload}", topic, payload);  

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(Encoding.UTF8.GetBytes(payload))
                .Build();

            await _client.PublishAsync(message, CancellationToken.None);
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await ConnectAsync();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return _client.DisconnectAsync();
        }

        public async Task SubscribeAsync(string topic)
        {
            await _client.SubscribeAsync(topic);
        }
    }
}