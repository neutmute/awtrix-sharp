using AwtrixSharpWeb.Apps.TripTimer;
using AwtrixSharpWeb.Controllers;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using AwtrixSharpWeb.Services.TripPlanner;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Reflection;
using Test.Apps;
using Test.HostedServices;
using Xunit;

namespace Test.Apps.TripTimer
{
    /// <summary>
    /// TripTimerController over a real Conductor (ConductorTestHelper) whose registry is seeded
    /// through the internal RegisterApp seam.
    /// </summary>
    public class TripTimerControllerTests
    {
        private static TripTimerApp CreateTripTimerApp(TimeSpan timeToOrigin, TimeSpan timeToPrepare)
        {
            var clock = new MockClock(DateTimeOffset.Parse("2025-08-19T05:30:00+10:00")); // pinned (CR-39)
            var mockLog = new Mock<ILogger>();
            var mockAwtrixService = new Mock<IAwtrixService>();
            var mockAddress = new AwtrixAddress { BaseTopic = "test/base/topic" };
            var mockTimerService = new Mock<ITimerService>();
            var mockTripPlannerService = new Mock<ITripPlannerService>();

            var config = new TripTimerAppConfig
            {
                CronSchedule = "* * * * *",
                ActiveTime = TimeSpan.FromMinutes(30),
                TimeToOrigin = timeToOrigin,
                TimeToPrepare = timeToPrepare,
            };
            // AppNames.TripTimerApp is an internal const ("TripTimerApp") on AwtrixSharpWeb.HostedServices.AppNames,
            // visible here via the awtrix-api InternalsVisibleTo("Test") entry.
            config.Type = AppNames.TripTimerApp;

            return new TripTimerApp(
                mockLog.Object,
                clock,
                mockAddress,
                mockAwtrixService.Object,
                mockTimerService.Object,
                config,
                mockTripPlannerService.Object);
        }

        private static Conductor CreateConductorWithApps(IEnumerable<IAwtrixApp> apps)
        {
            var conductor = ConductorTestHelper.Create();
            foreach (var app in apps)
            {
                conductor.RegisterApp(app);
            }
            return conductor;
        }

        [Fact]
        public void TestTimingConfig_ReturnsAlarmStagesComputedByTheRegisteredTripTimerApp()
        {
            // Arrange
            var timeToOrigin = TimeSpan.FromMinutes(14);
            var timeToPrepare = TimeSpan.FromMinutes(8);
            var app = CreateTripTimerApp(timeToOrigin, timeToPrepare);
            var conductor = CreateConductorWithApps(new IAwtrixApp[] { app });
            var sut = new TripTimerController(conductor);

            var departureTime = "2025-09-01 06:41";

            // Act
            var result = sut.TestTimingConfig(departureTime);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var alarmStagesType = okResult.Value.GetType();

            var originDepartTime = (DateTimeOffset)alarmStagesType.GetProperty("OriginDepartTime").GetValue(okResult.Value);
            var departForOriginTime = (DateTimeOffset)alarmStagesType.GetProperty("DepartForOriginTime").GetValue(okResult.Value);
            var prepareForDepartTime = (DateTimeOffset)alarmStagesType.GetProperty("PrepareForDepartTime").GetValue(okResult.Value);

            var expectedDeparture = DateTimeOffset.Parse(departureTime);
            Assert.Equal(expectedDeparture, originDepartTime);
            Assert.Equal(expectedDeparture.Add(-timeToOrigin), departForOriginTime);
            Assert.Equal(expectedDeparture.Add(-timeToOrigin).Add(-timeToPrepare), prepareForDepartTime);
        }

        [Fact]
        public void TestTimingConfig_UsesDefaultDepartureTime_WhenNoneSupplied()
        {
            // Arrange
            var app = CreateTripTimerApp(TimeSpan.Zero, TimeSpan.Zero);
            var conductor = CreateConductorWithApps(new IAwtrixApp[] { app });
            var sut = new TripTimerController(conductor);

            // Act - default parameter value is "2025-09-01 06:41"
            var result = sut.TestTimingConfig();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var alarmStagesType = okResult.Value.GetType();
            var originDepartTime = (DateTimeOffset)alarmStagesType.GetProperty("OriginDepartTime").GetValue(okResult.Value);
            Assert.Equal(DateTimeOffset.Parse("2025-09-01 06:41"), originDepartTime);
        }

        [Fact]
        public void TestTimingConfig_NoTripTimerAppRegistered_Returns404()
        {
            var conductor = CreateConductorWithApps(Array.Empty<IAwtrixApp>());
            var sut = new TripTimerController(conductor);

            var result = sut.TestTimingConfig("2025-09-01 06:41");

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public void StartNow_RunningApp_Returns200AndExecutesIt()
        {
            var app = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.TripTimerApp);
            var conductor = CreateConductorWithApps(new[] { app.Object });
            var sut = new TripTimerController(conductor);

            var result = sut.StartNow("awtrix/clock1", AppNames.TripTimerApp);

            Assert.IsType<OkObjectResult>(result);
            app.Verify(a => a.ExecuteNow(), Times.Once);
        }

        [Fact]
        public void StartNow_UnknownDevice_Returns404()
        {
            var app = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.TripTimerApp);
            var conductor = CreateConductorWithApps(new[] { app.Object });
            var sut = new TripTimerController(conductor);

            var result = sut.StartNow("awtrix/clock9", AppNames.TripTimerApp);

            Assert.IsType<NotFoundObjectResult>(result);
            app.Verify(a => a.ExecuteNow(), Times.Never);
        }

        [Fact]
        public void StartNow_AppThrows_Returns500()
        {
            var app = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.TripTimerApp);
            app.Setup(a => a.ExecuteNow()).Throws(new InvalidOperationException("boom"));
            var conductor = CreateConductorWithApps(new[] { app.Object });
            var sut = new TripTimerController(conductor);

            var result = sut.StartNow("awtrix/clock1", AppNames.TripTimerApp);

            Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);
        }

        [Fact]
        public void TestTimingConfig_InvalidDepartureTime_ReturnsBadRequest()
        {
            // CR-15: parsing happens before the app lookup, so an empty Conductor is enough
            var sut = new TripTimerController(ConductorTestHelper.Create());

            var result = sut.TestTimingConfig("not-a-date");

            Assert.IsType<BadRequestObjectResult>(result);
        }
    }
}
