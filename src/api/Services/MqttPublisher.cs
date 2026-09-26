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

        public override async Task<bool> Publish(AwtrixRequest request)
        {
            try
            {
                return await _mqttConnector.PublishAsync(request.Address, request.Payload);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "MQTT publish to {Topic} failed", request.Address);
                return false;
            }
        }
    }
}
