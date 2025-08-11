using MQTTnet;
using MQTTnet.Formatter;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AwtrixSharpWeb.Domain;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AwtrixSharpWeb.Services
{
    public class MqttService : IHostedService
    {
        private IMqttClient _client;
        private readonly ILogger<MqttService> _log;
        private readonly MqttSettings _settings;

        public MqttService(ILogger<MqttService> logger, IOptions<MqttSettings> settings)
        {
            _log = logger;
            _settings = settings.Value;
        }

        public async Task ConnectAsync()
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

            await _client.ConnectAsync(clientOptions, CancellationToken.None);
            _log.LogInformation("Connected to MQTT broker successfully");
        }

        public async Task PublishAsync(string topic, string payload)
        {
            _log.LogInformation("Publishing message to topic {Topic} with payload {Payload}", topic, payload);  

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