using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Services;

namespace Test.Services
{
    /// <summary>
    /// Covers AwtrixService's topic construction, payload serialization and
    /// HTTP-vs-MQTT publisher selection. Uses FakeHttpPublisher/FakeMqttPublisher
    /// (see FakePublishers.cs) so no real network/broker calls are made.
    /// </summary>
    public class AwtrixServicePublishTests
    {
        private static (AwtrixService service, FakeHttpPublisher http, FakeMqttPublisher mqtt) CreateService()
        {
            var http = new FakeHttpPublisher();
            var mqtt = new FakeMqttPublisher();
            var service = new AwtrixService(http, mqtt);
            return (service, http, mqtt);
        }

        [Fact]
        public async Task Notify_WithText_PublishesToNotifyTopic()
        {
            var (service, http, mqtt) = CreateService();
            var address = new AwtrixAddress { BaseTopic = "awtrix/clock1" };
            var message = new AwtrixAppMessage().SetText("Hello");

            var result = await service.Notify(address, message);

            Assert.True(result);
            Assert.Equal("awtrix/clock1/notify", mqtt.LastUrl);
            Assert.Equal(0, http.PublishCallCount);
            Assert.Contains("\"text\":\"Hello\"", mqtt.LastPayload);
        }

        [Fact]
        public async Task Notify_WithEmptyText_DelegatesToDismiss()
        {
            var (service, http, mqtt) = CreateService();
            var address = new AwtrixAddress { BaseTopic = "awtrix/clock1" };
            var message = new AwtrixAppMessage();

            var result = await service.Notify(address, message);

            Assert.True(result);
            Assert.Equal("awtrix/clock1/notify/dismiss", mqtt.LastUrl);
            Assert.Equal(string.Empty, mqtt.LastPayload);
        }

        [Fact]
        public async Task Notify_WithWhitespaceText_DelegatesToDismiss()
        {
            var (service, http, mqtt) = CreateService();
            var address = new AwtrixAddress { BaseTopic = "awtrix/clock1" };
            var message = new AwtrixAppMessage().SetText("   ");

            await service.Notify(address, message);

            Assert.Equal("awtrix/clock1/notify/dismiss", mqtt.LastUrl);
        }

        [Fact]
        public async Task Dismiss_PublishesEmptyPayloadToDismissTopic()
        {
            var (service, _, mqtt) = CreateService();
            var address = new AwtrixAddress { BaseTopic = "awtrix/clock1" };

            var result = await service.Dismiss(address);

            Assert.True(result);
            Assert.Equal("awtrix/clock1/notify/dismiss", mqtt.LastUrl);
            Assert.Equal(string.Empty, mqtt.LastPayload);
        }

        [Fact]
        public async Task AppUpdate_PublishesToCustomAppTopic()
        {
            var (service, _, mqtt) = CreateService();
            var address = new AwtrixAddress { BaseTopic = "awtrix/clock1" };
            var message = new AwtrixAppMessage().SetText("42");

            var result = await service.AppUpdate(address, "MyApp", message);

            Assert.True(result);
            Assert.Equal("awtrix/clock1/custom/MyApp", mqtt.LastUrl);
            Assert.Contains("42", mqtt.LastPayload);
        }

        [Fact]
        public async Task AppClear_PublishesEmptyPayloadToCustomAppTopic()
        {
            var (service, _, mqtt) = CreateService();
            var address = new AwtrixAddress { BaseTopic = "awtrix/clock1" };

            var result = await service.AppClear(address, "MyApp");

            Assert.True(result);
            Assert.Equal("awtrix/clock1/custom/MyApp", mqtt.LastUrl);
            Assert.Equal(string.Empty, mqtt.LastPayload);
        }

        [Fact]
        public async Task Set_PublishesSettingsJsonToSettingsTopic()
        {
            var (service, _, mqtt) = CreateService();
            var address = new AwtrixAddress { BaseTopic = "awtrix/clock1" };
            var settings = new AwtrixSettings().SetBrightness(128);

            var result = await service.Set(address, settings);

            Assert.True(result);
            Assert.Equal("awtrix/clock1/settings", mqtt.LastUrl);
            Assert.Equal("{\"BRI\":\"128\"}", mqtt.LastPayload);
        }

        [Fact]
        public async Task PlayRtttl_PublishesRawStringToRtttlTopic()
        {
            var (service, _, mqtt) = CreateService();
            var address = new AwtrixAddress { BaseTopic = "awtrix/clock1" };

            var result = await service.PlayRtttl(address, "d=4,o=5,b=140:8g,8a");

            Assert.True(result);
            Assert.Equal("awtrix/clock1/rtttl", mqtt.LastUrl);
            // PlayRtttl publishes the raw string directly - it is NOT JSON-wrapped.
            Assert.Equal("d=4,o=5,b=140:8g,8a", mqtt.LastPayload);
        }

        [Fact]
        public async Task HttpBaseTopic_RoutesToHttpPublisher()
        {
            var (service, http, mqtt) = CreateService();
            var address = new AwtrixAddress { BaseTopic = "http://192.168.1.50" };

            await service.Dismiss(address);

            Assert.Equal(1, http.PublishCallCount);
            Assert.Equal(0, mqtt.PublishCallCount);
            Assert.Equal("http://192.168.1.50/notify/dismiss", http.LastUrl);
        }

        [Fact]
        public async Task HttpsBaseTopic_RoutesToHttpPublisher()
        {
            var (service, http, mqtt) = CreateService();
            var address = new AwtrixAddress { BaseTopic = "https://192.168.1.50" };

            await service.Notify(address, new AwtrixAppMessage().SetText("hi"));

            Assert.Equal(1, http.PublishCallCount);
            Assert.Equal(0, mqtt.PublishCallCount);
        }

        [Fact]
        public async Task NonHttpBaseTopic_RoutesToMqttPublisher()
        {
            var (service, http, mqtt) = CreateService();
            var address = new AwtrixAddress { BaseTopic = "awtrix/clock1" };

            await service.Dismiss(address);

            Assert.Equal(0, http.PublishCallCount);
            Assert.Equal(1, mqtt.PublishCallCount);
        }

        [Fact]
        public async Task FailedPublish_PropagatesFalseResult()
        {
            var (service, _, mqtt) = CreateService();
            mqtt.ReturnValue = false;
            var address = new AwtrixAddress { BaseTopic = "awtrix/clock1" };

            var result = await service.Dismiss(address);

            Assert.False(result);
        }

        [Fact]
        public async Task PublisherThatThrows_IsContainedAndReportedAsFalse()
        {
            var (service, _, mqtt) = CreateService();
            mqtt.ThrowOnPublish = new InvalidOperationException("contract violation");
            var address = new AwtrixAddress { BaseTopic = "awtrix/clock1" };

            var notify = await service.Notify(address, new AwtrixAppMessage().SetText("hi"));
            var update = await service.AppUpdate(address, "MyApp", new AwtrixAppMessage().SetText("42"));
            var set = await service.Set(address, new AwtrixSettings().SetBrightness(5));

            Assert.False(notify);
            Assert.False(update);
            Assert.False(set);
        }

        [Fact]
        public async Task HttpBaseTopic_AppUpdate_UsesCustomQueryNameUrl()
        {
            var (service, http, mqtt) = CreateService();
            var address = new AwtrixAddress { BaseTopic = "http://192.168.1.50/api" };

            var result = await service.AppUpdate(address, "TripTimerApp", new AwtrixAppMessage().SetText("42"));

            Assert.True(result);
            Assert.Equal("http://192.168.1.50/api/custom?name=TripTimerApp", http.LastUrl);
            Assert.Contains("42", http.LastPayload);
            Assert.Equal(0, mqtt.PublishCallCount);
        }

        [Fact]
        public async Task HttpBaseTopicWithTrailingSlash_AppClear_UsesCustomQueryNameUrlWithEmptyPayload()
        {
            var (service, http, _) = CreateService();
            var address = new AwtrixAddress { BaseTopic = "http://192.168.1.50/api/" };

            await service.AppClear(address, "TripTimerApp");

            Assert.Equal("http://192.168.1.50/api/custom?name=TripTimerApp", http.LastUrl);
            Assert.Equal(string.Empty, http.LastPayload);
        }

        [Fact]
        public async Task HttpBaseTopic_Settings_PathUnchanged()
        {
            var (service, http, _) = CreateService();
            var address = new AwtrixAddress { BaseTopic = "http://192.168.1.50/api" };

            await service.Set(address, new AwtrixSettings().SetBrightness(8));

            Assert.Equal("http://192.168.1.50/api/settings", http.LastUrl);
        }

        [Fact]
        public async Task UppercaseHttpScheme_RoutesToHttpPublisher()
        {
            var (service, http, mqtt) = CreateService();
            var address = new AwtrixAddress { BaseTopic = "HTTP://192.168.1.50/api" };

            await service.Dismiss(address);

            Assert.Equal(1, http.PublishCallCount);
            Assert.Equal(0, mqtt.PublishCallCount);
        }
    }
}
