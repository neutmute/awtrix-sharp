using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using MQTTnet;
using System.Text;

namespace AwtrixSharpWeb.Apps
{
    public class MqttApp : ScheduledApp<MqttAppConfig>
    {
        IMqttConnector _mqttConnector;

        public MqttApp(
         ILogger logger
         ,IClock clock
         ,MqttAppConfig config
         ,AwtrixAddress awtrixAddress
         ,IAwtrixService awtrixService
         ,IMqttConnector mqttConnector) : base(logger, clock, awtrixAddress, awtrixService, config)
        {
            _mqttConnector = mqttConnector;
        }

        protected override Task ActivateScheduledWork(CancellationTokenSource cts)
        {
            _mqttConnector.Subscribe(Config.ReadTopic);
            _mqttConnector.MessageReceived += MessageReceived;
            return Task.CompletedTask;
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

            return Notify(message);
        }
    }
}
