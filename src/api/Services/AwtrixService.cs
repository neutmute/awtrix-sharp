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

        /// <summary>
        /// https://blueforcer.github.io/awtrix3/#/api?id=change-settings
        /// </summary>
        public async Task<bool> Set(AwtrixAddress awtrixAddress, AwtrixSettings settings)
        {
            var payload = settings.ToJson();
            return await ResolvePublisher(awtrixAddress.BaseTopic).Publish(awtrixAddress.BaseTopic + $"/settings", payload);
        }


        /// <summary>
        /// https://blueforcer.github.io/awtrix3/#/api?id=sound-playback
        /// </summary>
        public async Task<bool> PlayRtttl(AwtrixAddress awtrixAddress, string rtttl)
        {
            return await ResolvePublisher(awtrixAddress.BaseTopic).Publish(awtrixAddress.BaseTopic + $"/rtttl", rtttl);
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

        /// <summary>
        /// Blink acts as a sign of life
        /// </summary>
        public static (int quantized, int quantizedBlink) Quantize(int progress)
        {
            int p = Math.Clamp(progress, 0, 100);

            int LedCount(int v)
            {
                if (v < 4) return 0;
                if (v == 100) return 32;
                return 1 + (int)Math.Floor((v - 4) * 31.0 / 96.0); // 4–99 => 1–31
            }

            int n = LedCount(p);
            int blink;

            if (n == 0) blink = 4;            // nothing lit
            else if (n == 32) blink = 99;     // drop to 31 LEDs
            else if (p < 4) blink = 1; // one bin lower
            else
            {
                int lowerBound = (n == 1) ? 4 : (int)Math.Ceiling(4 + 96.0 * (n - 1) / 31.0);
                blink = Math.Clamp(lowerBound - 1, 0, 99); // one bin lower
            }

            return (p, blink);
        }


        /// <remarks>https://blueforcer.github.io/awtrix3/#/api?id=dismiss-notification</remarks>
        public async Task<bool> Dismiss(AwtrixAddress awtrixAddress)
        {
            return await Publish(awtrixAddress.BaseTopic + "/notify/dismiss", null);
        }

        private async Task<bool> Publish(string topic, AwtrixAppMessage? message)
        {
            return await ResolvePublisher(topic).Publish(topic, message);
            
        }

        private AwtrixPublisher ResolvePublisher(string topic)
        {
            if (topic.StartsWith("http://") || topic.StartsWith("https://"))
            {
                return _httpPublisher;
            }
            else
            {
                return _mqttPublisher;
            }
        }
    }
}
