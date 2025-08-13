using AwtrixSharpWeb.Domain;

namespace AwtrixSharpWeb.Services
{
    public class AwtrixService
    {
        MqttService _mqttService;

        public AwtrixService(MqttService mqttService)
        {
            _mqttService = mqttService;
        }

        public async Task<bool> AppUpdate(AwtrixAddress awtrixAddress, string appName, AwtrixAppMessage2 message)
        {
            return await Publish(awtrixAddress.BaseTopic + $"/custom/{appName}", message);
        }

        public async Task<bool> AppClear(AwtrixAddress awtrixAddress, string appName)
        {
            return await Publish(awtrixAddress.BaseTopic + $"/custom/{appName}", null);
        }

        public async Task<bool> Notify(AwtrixAddress awtrixAddress, AwtrixAppMessage2 message)
        {
            return await Publish(awtrixAddress.BaseTopic + "/notify", message);
        }

        private async Task<bool> Publish(string topic, AwtrixAppMessage2? message)
        {
            var payload = string.Empty;
            if (message != null)
            {
                payload = System.Text.Json.JsonSerializer.Serialize(message);
            }
            await _mqttService.PublishAsync(topic, payload);
            return true;
        }
    }
}
