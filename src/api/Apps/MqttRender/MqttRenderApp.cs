using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using MQTTnet;
using System.Text;

namespace AwtrixSharpWeb.Apps.MqttRender
{
    /// <summary>
    /// Render a subscribed MQTT payload while the scheduled window is active
    /// </summary>
    public class MqttRenderApp : ScheduledApp<MqttAppConfig>
    {
        IMqttConnector _mqttConnector;

        public MqttRenderApp(
         ILogger logger
         , IClock clock
         , MqttAppConfig config
         , AwtrixAddress awtrixAddress
         , IAwtrixService awtrixService
         , IMqttConnector mqttConnector) : base(logger, clock, awtrixAddress, awtrixService, config)
        {
            _mqttConnector = mqttConnector;
        }

        protected override async Task OnActivateAsync(ScheduledActivation activation)
        {
            // Attach first: a retained message can arrive before Subscribe returns (CR-33)
            _mqttConnector.MessageReceived += RawMessageReceived;

            // A SUBACK that never arrives must not keep the window open past ActiveTime
            await _mqttConnector.Subscribe(Config.ReadTopic).WaitAsync(activation.Token);
        }

        protected override Task OnDeactivateAsync(ScheduledActivation activation)
        {
            _mqttConnector.MessageReceived -= RawMessageReceived;
            return Task.CompletedTask;
        }

        private Task RawMessageReceived(MqttApplicationMessageReceivedEventArgs arg)
        {
            if (CurrentActivation is not { IsEnded: false })
            {
                return Task.CompletedTask; // a dispatch already in flight when the window ended
            }

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
