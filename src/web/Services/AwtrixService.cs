using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;

namespace AwtrixSharpWeb.Services
{
    public class AwtrixService
    {
        MqttConnector _mqttService;

        public AwtrixService(MqttConnector mqttService)
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
            if (String.IsNullOrWhiteSpace(message.Text))
            {
                return await Dismiss(awtrixAddress);
            }
            else
            {
                return await Publish(awtrixAddress.BaseTopic + "/notify", message);
            }
        }


        /// <remarks>https://blueforcer.github.io/awtrix3/#/api?id=dismiss-notification</remarks>
        public async Task<bool> Dismiss(AwtrixAddress awtrixAddress)
        {
            return await Publish(awtrixAddress.BaseTopic + "/notify/dismiss", null);
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
