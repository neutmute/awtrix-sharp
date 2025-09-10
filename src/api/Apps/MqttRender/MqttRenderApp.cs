using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using MQTTnet;
using System.Text;

namespace AwtrixSharpWeb.Apps.MqttRender
{

    /// <summary>
    /// Render a subscribed MQTT payload
    /// </summary>
    public class MqttRenderApp : ScheduledApp<MqttAppConfig>
    {
        IMqttConnector _mqttConnector;

        public MqttRenderApp(
         ILogger logger
         ,IClock clock
         ,MqttAppConfig config
         ,AwtrixAddress awtrixAddress
         ,IAwtrixService awtrixService
         ,IMqttConnector mqttConnector) : base(logger, clock, awtrixAddress, awtrixService, config)
        {
            _mqttConnector = mqttConnector;
        }

        protected override async Task ActivateScheduledWork(CancellationTokenSource cts)
        {
            await _mqttConnector.Subscribe(Config.ReadTopic);
            _mqttConnector.MessageReceived += RawMessageReceived;

            try
            {
                await WaitForCancellation(cts.Token);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error in ActivateScheduledWork: {ex.Message}");
            }
            finally
            {
                await Deactivate();
            }
        }

        private async Task Deactivate()
        {
            _mqttConnector.MessageReceived -= RawMessageReceived;
            await AppClear();
        }

        /// <summary>
        /// Make sure we are a subscriber to this topic before continuing
        /// </summary>
        private Task RawMessageReceived(MqttApplicationMessageReceivedEventArgs arg)
        {
            // the client can be subscribed to multiple topics, so we need to filter here
            if (arg.ApplicationMessage.Topic == Config.ReadTopic)
            {
                return HandleMessage(arg);
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// If invoked, then this is the correct topic
        /// </summary>
        protected virtual Task HandleMessage(MqttApplicationMessageReceivedEventArgs arg)
        {
            string textPayload = Encoding.UTF8.GetString(arg.ApplicationMessage.Payload);

            var message = new AwtrixAppMessage()
                            .SetText(textPayload);

            var valueMap = Config.FindMatchingValueMap(textPayload);

            if (valueMap != null)
            {
                Logger.LogDebug("Found matching value map for status: {StatusText}", textPayload);

                valueMap.Decorate(message, Logger);

                // If no text is set in the mapping, use the original status text
                if (message.Text == null)
                {
                    message.SetText(textPayload);
                }
            }

            return AppUpdate(message);
        }
    }
}
