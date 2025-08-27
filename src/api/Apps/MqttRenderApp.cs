using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using MQTTnet;
using System.Text;

namespace AwtrixSharpWeb.Apps
{
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
            _mqttConnector.MessageReceived += MessageReceived;

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
            _mqttConnector.MessageReceived -= MessageReceived;
            await AppClear();
        }

        private Task MessageReceived(MqttApplicationMessageReceivedEventArgs arg)
        {
            string payload = Encoding.UTF8.GetString(arg.ApplicationMessage.Payload);


            var message = new AwtrixAppMessage()
                            .SetText(payload)
                            .SetIcon(Config.Icon);

            if (payload.StartsWith("-"))
            {
                message.SetColor("#FF0000");
            }
            else
            {
                message.SetColor("#FFFF00");
            }

            return AppUpdate(message);
        }
    }
}
