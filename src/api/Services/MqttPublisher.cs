using AwtrixSharpWeb.Interfaces;

namespace AwtrixSharpWeb.Services
{
    /// <summary>
    /// Publishes to an Awtrix device over MQTT. Never throws: failures are reported as false.
    /// </summary>
    public class MqttPublisher : AwtrixPublisher
    {
        private readonly IMqttConnector _mqttConnector;

        public MqttPublisher(IMqttConnector mqttConnector, ILogger<MqttPublisher> logger) : base(logger)
        {
            _mqttConnector = mqttConnector;
        }

        public override async Task<bool> Publish(string topic, string payload)
        {
            try
            {
                return await _mqttConnector.PublishAsync(topic, payload);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "MQTT publish to {Topic} failed", topic);
                return false;
            }
        }
    }
}
