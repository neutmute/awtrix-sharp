using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using MQTTnet;
using System.Text;

namespace AwtrixSharpWeb.Apps.MqttRender
{
    /// <summary>
    /// Render the time and an MQTT message
    /// </summary>
    public class MqttClockRenderApp : MqttRenderApp
    {
        ITimerService _timerService;
        string _mqttValue = "";
        DateTime _currentTime = DateTime.MinValue;

        public MqttClockRenderApp(
             ILogger logger
             , IClock clock
             , MqttAppConfig config
             , AwtrixAddress awtrixAddress
             , IAwtrixService awtrixService
             , IMqttConnector mqttConnector
            , ITimerService timerService) : base(logger, clock, config, awtrixAddress, awtrixService, mqttConnector)
        {
            _timerService = timerService;
        }

        protected override async Task ActivateScheduledWork(CancellationTokenSource cts)
        {
            _timerService.SecondChanged += ClockTick;

            await base.ActivateScheduledWork(cts);
        }

        private void ClockTick(object? sender, ClockTickEventArgs e)
        {
            _currentTime= e.Time;
            UpdateDisplay();
        }

        protected override async Task MessageReceived(MqttApplicationMessageReceivedEventArgs arg)
        {
            _mqttValue = Encoding.UTF8.GetString(arg.ApplicationMessage.Payload);
            await UpdateDisplay();
        }

        private Task<bool> UpdateDisplay()
        {
            var clockText = TimerService.FormatClockString(_currentTime, true);

            var messageText = $"{clockText} {_mqttValue}";

            var message = new AwtrixAppMessage()
                                .SetText(messageText)
                                .SetDuration(3600);

            return AppUpdate(message);
        }
    }
}
