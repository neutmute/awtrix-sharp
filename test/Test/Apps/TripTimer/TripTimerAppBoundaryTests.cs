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

        private TripTimerApp GetSystemUnderTest(DateTimeOffset now, DateTimeOffset departureTime)
        {
            _clock = new MockClock(now);
            _mockLog = new Mock<ILogger>();
            _mockAwtrixService = new Mock<IAwtrixService>();
            _mockAddress = new AwtrixAddress { BaseTopic = "test/base/topic" };
            _mockTimerService = new Mock<ITimerService>();
            _mockTripPlannerService = new Mock<ITripPlannerService>();

            _mockTripPlannerService
                .Setup(x => x.GetNextDepartures(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()))
                .ReturnsAsync(new List<TripSummary> { TripSummaryTests.Create(departureTime) });

            _timerConfig = new TripTimerAppConfig
            {
                CronSchedule = "* * * * *",
                ActiveTime = TimeSpan.FromMinutes(30),
                TimeToOrigin = TimeSpan.Zero,
                TimeToPrepare = TimeSpan.Zero,
            };
            _timerConfig.Type = "TripTimer";

            var sut = new TripTimerApp(
                _mockLog.Object,
                _clock,
                _mockAddress,
                _mockAwtrixService.Object,
                _mockTimerService.Object,
                _timerConfig,
                _mockTripPlannerService.Object);

            // BuildMessage cancels `_cts` when there are no future departures; ActivateScheduledWork
            // (which normally creates it) is never invoked in these unit tests, so it must be seeded
            // via reflection to avoid a NullReferenceException on that code path.
            var ctsField = typeof(TripTimerApp).GetField("_cts", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            ctsField.SetValue(sut, new System.Threading.CancellationTokenSource());

            // TimeToOrigin/TimeToPrepare are zero above, so the alarm time equals the departure time.
            sut.NextDepartures.Clear();
            sut.NextDepartures.Add(TripSummaryTests.Create(departureTime));

            return sut;
        }

        private static AwtrixAppMessage InvokeBuildMessage(TripTimerApp sut, DateTime tickTime)
        {
            var method = typeof(TripTimerApp).GetMethod("BuildMessage", BindingFlags.NonPublic | BindingFlags.Instance);
            var args = new object[] { new ClockTickEventArgs(tickTime) };
            return (AwtrixAppMessage)method.Invoke(sut, args);
        }

        [Fact]
        public void BuildMessage_NoFutureDepartures_ReturnsEmptyMessage()
        {
            // Arrange - departure (and therefore alarm) is in the past relative to "now"
            var departureTime = DateTimeOffset.Parse("2025-08-19T06:41:00+10:00");
            var now = departureTime.AddMinutes(5);
            var sut = GetSystemUnderTest(now, departureTime);

            // Act
            var message = InvokeBuildMessage(sut, now.DateTime);

            // Assert
            Assert.Empty(message);
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
            Assert.Contains("\"c\": \"00FF00\"", message.Text);
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
            Assert.Contains("\"c\": \"FFA500\"", message.Text);
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
            Assert.Contains("\"c\": \"FFA500\"", message.Text);
        }

        [Fact]
        public void BuildMessage_AtVisualAlertBufferBoundary_StillShowsCountdown_NotGo()
        {
            // Arrange - VisualAlertBuffer is exactly 20 seconds; the check is `timeToAlarm < VisualAlertBuffer`
            // so at exactly 20 seconds remaining the "GO!" alert must NOT yet trigger.
            var departureTime = DateTimeOffset.Parse("2025-08-19T06:41:00+10:00");
            var now = departureTime.AddSeconds(-20);
            var sut = GetSystemUnderTest(now, departureTime);

            // Act
            var message = InvokeBuildMessage(sut, now.DateTime);

            // Assert
            Assert.NotEqual("GO!", message.Text);
        }

        [Fact]
        public void BuildMessage_InsideVisualAlertBuffer_WithNoValueMaps_ShowsGoAlert()
        {
            // Arrange - just inside the 20 second buffer
            var departureTime = DateTimeOffset.Parse("2025-08-19T06:41:00+10:00");
            var now = departureTime.AddSeconds(-19);
            var sut = GetSystemUnderTest(now, departureTime);

            // Act
            var message = InvokeBuildMessage(sut, now.DateTime);

            // Assert
            Assert.Equal("GO!", message.Text);
            Assert.Equal("true", message["rainbow"]);
            Assert.Equal("100", message["progress"]);
        }

        [Fact]
        public void BuildMessage_InsideVisualAlertBuffer_WithValueMapConfigured_UsesValueMapDecoration_InsteadOfGoAlert()
        {
            // Arrange
            var departureTime = DateTimeOffset.Parse("2025-08-19T06:41:00+10:00");
            var now = departureTime.AddSeconds(-19);
            var sut = GetSystemUnderTest(now, departureTime);

            var valueMap = new ValueMap { ["Text"] = "ALMOST THERE", ["Color"] = "00FFFF" };
            _timerConfig.ValueMaps.Add(valueMap);

            // Act
            var message = InvokeBuildMessage(sut, now.DateTime);

            // Assert
            Assert.Equal("ALMOST THERE", message.Text);
            Assert.Equal("00FFFF", message["color"]);
        }
    }
}
