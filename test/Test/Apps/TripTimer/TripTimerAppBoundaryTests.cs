using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Apps.TripTimer;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using AwtrixSharpWeb.Services.TripPlanner;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Reflection;
using Test.Apps;
using Test.Domain;
using Xunit;

namespace Test.Apps.TripTimer
{
    /// <summary>
    /// Exercises the countdown text/colour/progress output produced by TripTimerApp.BuildMessage at
    /// the various time boundaries it switches behaviour on (1 minute before the alarm, and the final
    /// VisualAlertBuffer/"GO!" window). BuildMessage is private, so it is invoked via reflection - the
    /// class exposes no other seam for observing the composed AwtrixAppMessage.
    /// </summary>
    public class TripTimerAppBoundaryTests
    {
        private MockClock _clock;
        private Mock<ILogger> _mockLog;
        private AwtrixAddress _mockAddress;
        private Mock<IAwtrixService> _mockAwtrixService;
        private Mock<ITimerService> _mockTimerService;
        private Mock<ITripPlannerService> _mockTripPlannerService;
        private TripTimerAppConfig _timerConfig;

        private TripTimerApp GetSystemUnderTest(DateTimeOffset now, DateTimeOffset departureTime, TimeSpan? alertDuration = null)
        {
            _clock = new MockClock(now);
            _mockLog = new Mock<ILogger>();
            _mockAwtrixService = new Mock<IAwtrixService>();
            _mockAddress = new AwtrixAddress { BaseTopic = "test/base/topic" };
            _mockTimerService = new Mock<ITimerService>();
            _mockTripPlannerService = new Mock<ITripPlannerService>();

            _mockTripPlannerService
                .Setup(x => x.GetNextDepartures(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<TripSummary> { TripSummaryTests.Create(departureTime) });

            _timerConfig = new TripTimerAppConfig
            {
                CronSchedule = "* * * * *",
                ActiveTime = TimeSpan.FromMinutes(30),
                TimeToOrigin = TimeSpan.Zero,
                TimeToPrepare = TimeSpan.Zero,
            };
            _timerConfig.Type = "TripTimer";
            if (alertDuration.HasValue)
            {
                _timerConfig.AlertDuration = alertDuration.Value;
            }

            var sut = new TripTimerApp(
                _mockLog.Object,
                _clock,
                _mockAddress,
                _mockAwtrixService.Object,
                _mockTimerService.Object,
                _timerConfig,
                _mockTripPlannerService.Object);

            // TimeToOrigin/TimeToPrepare are zero above, so the alarm time equals the departure time.
            sut.SetDepartures(new[] { TripSummaryTests.Create(departureTime) });

            return sut;
        }

        private static AwtrixAppMessage InvokeBuildMessage(TripTimerApp sut, DateTime tickTime)
        {
            var message = sut.BuildMessage(tickTime);
            Assert.NotNull(message);
            return message!;
        }

        [Fact]
        public void BuildMessage_NoFutureDepartures_ReturnsNull()
        {
            // CR-19: null means "nothing to show"; the tick handler completes the activation instead of publishing {}
            var departureTime = DateTimeOffset.Parse("2025-08-19T06:41:00+10:00");
            var now = departureTime.AddMinutes(5);
            var sut = GetSystemUnderTest(now, departureTime);

            Assert.Null(sut.BuildMessage(now.DateTime));
        }

        [Fact]
        public void BuildMessage_MoreThanOneMinuteBeforeAlarm_UsesGreenNowColor()
        {
            // Arrange
            var departureTime = DateTimeOffset.Parse("2025-08-19T06:41:00+10:00");
            var now = departureTime.AddSeconds(-61);
            var sut = GetSystemUnderTest(now, departureTime);

            // Act
            var message = InvokeBuildMessage(sut, now.DateTime);

            // Assert
            Assert.Equal("#00FF00", Assert.IsType<TextFragment[]>(message["text"])[0].Color);
        }

        [Fact]
        public void BuildMessage_ExactlyOneMinuteBeforeAlarm_SwitchesToOrangeNowColor()
        {
            // Arrange - boundary: nextAlarm.AddMinutes(-1) <= Clock.Now
            var departureTime = DateTimeOffset.Parse("2025-08-19T06:41:00+10:00");
            var now = departureTime.AddMinutes(-1);
            var sut = GetSystemUnderTest(now, departureTime);

            // Act
            var message = InvokeBuildMessage(sut, now.DateTime);

            // Assert
            Assert.Equal("#FFA500", Assert.IsType<TextFragment[]>(message["text"])[0].Color);
        }

        [Fact]
        public void BuildMessage_JustInsideLastMinute_UsesOrangeNowColor()
        {
            // Arrange
            var departureTime = DateTimeOffset.Parse("2025-08-19T06:41:00+10:00");
            var now = departureTime.AddSeconds(-59);
            var sut = GetSystemUnderTest(now, departureTime);

            // Act
            var message = InvokeBuildMessage(sut, now.DateTime);

            // Assert
            Assert.Equal("#FFA500", Assert.IsType<TextFragment[]>(message["text"])[0].Color);
        }

        [Fact]
        public void BuildMessage_AtVisualAlertBufferBoundary_StillShowsCountdown_NotGo()
        {
            // Arrange - the default AlertDuration is exactly 40 seconds; the check is `timeToAlarm < VisualAlertBuffer`
            // so at exactly 40 seconds remaining the "GO!" alert must NOT yet trigger.
            var departureTime = DateTimeOffset.Parse("2025-08-19T06:41:00+10:00");
            var now = departureTime.AddSeconds(-40);
            var sut = GetSystemUnderTest(now, departureTime);

            // Act
            var message = InvokeBuildMessage(sut, now.DateTime);

            // Assert
            Assert.IsType<TextFragment[]>(message["text"]);
        }

        [Theory]
        [InlineData(89, true)]
        [InlineData(90, false)]
        public void BuildMessage_ConfiguredAlertDuration_SetsWhenTheAlertStarts(int secondsBeforeAlarm, bool expectAlert)
        {
            var departureTime = DateTimeOffset.Parse("2025-08-19T06:41:00+10:00");
            var now = departureTime.AddSeconds(-secondsBeforeAlarm);
            var sut = GetSystemUnderTest(now, departureTime, TimeSpan.FromSeconds(90));

            var message = InvokeBuildMessage(sut, now.DateTime);

            Assert.Equal(expectAlert, message.Text == "GO!");
        }

        [Fact]
        public void BuildMessage_InsideVisualAlertBuffer_WithNoValueMaps_ShowsGoAlert()
        {
            // Arrange - just inside the default 40 second buffer
            var departureTime = DateTimeOffset.Parse("2025-08-19T06:41:00+10:00");
            var now = departureTime.AddSeconds(-39);
            var sut = GetSystemUnderTest(now, departureTime);

            // Act
            var message = InvokeBuildMessage(sut, now.DateTime);

            // Assert
            Assert.Equal("GO!", message.Text);
            Assert.Equal("Rainbow", message["palette"]);
            Assert.Equal("palette", message["textColor"]);
            Assert.Equal(100, message["progress"]);
        }

        [Fact]
        public void BuildMessage_InsideVisualAlertBuffer_WithValueMapConfigured_UsesValueMapDecoration_InsteadOfGoAlert()
        {
            // Arrange
            var departureTime = DateTimeOffset.Parse("2025-08-19T06:41:00+10:00");
            var now = departureTime.AddSeconds(-39);
            var sut = GetSystemUnderTest(now, departureTime);

            var valueMap = new ValueMap { ["Text"] = "ALMOST THERE", ["TextColor"] = "#00FFFF" };
            _timerConfig.ValueMaps.Add(valueMap);

            // Act
            var message = InvokeBuildMessage(sut, now.DateTime);

            // Assert
            Assert.Equal("ALMOST THERE", message.Text);
            Assert.Equal("#00FFFF", message["textColor"]);
        }
    }
}
