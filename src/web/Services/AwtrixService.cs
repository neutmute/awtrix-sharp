using AwtrixSharpWeb.Domain;

namespace AwtrixSharpWeb.Services
{


    public class AwtrixService : IAwtrixService
    {
        HttpPublisher _httpPublisher;
        MqttPublisher _mqttPublisher;

        public AwtrixService(HttpPublisher httpPublisher, MqttPublisher mqttPublisher)
        {
            _httpPublisher = httpPublisher;
            _mqttPublisher = mqttPublisher;
        }

        public async Task<bool> AppUpdate(AwtrixAddress awtrixAddress, string appName, AwtrixAppMessage message)
        {
            return await Publish(awtrixAddress.BaseTopic + $"/custom/{appName}", message);
        }

        public async Task<bool> AppClear(AwtrixAddress awtrixAddress, string appName)
        {
            return await Publish(awtrixAddress.BaseTopic + $"/custom/{appName}", null);
        }

        public async Task<bool> Notify(AwtrixAddress awtrixAddress, AwtrixAppMessage message)
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

        private async Task<bool> Publish(string topic, AwtrixAppMessage? message)
        {
            if (topic.StartsWith("http://") || topic.StartsWith("https://"))
            {
                return await _httpPublisher.Publish(topic, message);
            }
            else
            {
                return await _mqttPublisher.Publish(topic, message);
            }
        }
    }
}
