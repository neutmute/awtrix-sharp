using AwtrixSharpWeb.Apps.Configs;

namespace AwtrixSharpWeb.Apps.MqttRender
{
    public class MqttAppConfig : ScheduledAppConfig
    {
        public string ReadTopic
        {
            get => GetConfig<string>("ReadTopic");
            set => SetConfig("ReadTopic", value);
        }
    }
}
