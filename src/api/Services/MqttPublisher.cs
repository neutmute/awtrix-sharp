using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using Microsoft.Extensions.Logging;

namespace AwtrixSharpWeb.Services
{
    public class MqttPublisher : AwtrixPublisher
    {
        MqttConnector _mqttService;

        public MqttPublisher(MqttConnector mqttService, ILogger<MqttPublisher> logger) : base(logger)
        {
            _mqttService = mqttService;
        }

        public override async Task<bool> Publish(string topic, string payload)
        {
            await _mqttService.PublishAsync(topic, payload);
            return true;
        }
    }
}
