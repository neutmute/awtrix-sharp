using AwtrixSharpWeb.Apps.Configs;

namespace AwtrixSharpWeb.Apps.MqttRender
{
    public class MqttAppConfig : ScheduledAppConfig
    {
        public string Icon
        {
            get => GetConfig<string>("Icon");
            set => SetConfig("Icon", value);
        }

        public string ReadTopic
        {
            get => GetConfig<string>("ReadTopic");
            set => SetConfig("ReadTopic", value);
        }
    }
}
