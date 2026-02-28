# Testing Patterns

**Analysis Date:** 2026-02-28

## Test Framework

**Runner:**
- xUnit 2.9.2 (main test project `test/Test/Test.csproj`)
- xUnit 2.7.0 (transport library test project `test/transportOpenData.Tests/TransportOpenData.Tests.csproj`)
- Config: No `xunit.runner.json` detected — default xUnit runner settings

**Assertion Library:**
- xUnit built-in assertions (`Assert.Equal`, `Assert.NotNull`, `Assert.True`, etc.)
- No FluentAssertions or Shouldly detected

**Mocking:**
- Moq 4.20.72 — used in `test/Test/` project only
- `test/transportOpenData.Tests/` has no mocking dependency (pure deserialization tests)

**Coverage:**
- `coverlet.collector` 6.0.2 in both test projects

**Run Commands:**
```bash
dotnet test                                                        # Run all tests
dotnet test test/Test/Test.csproj                                  # Run main API tests
dotnet test test/transportOpenData.Tests/TransportOpenData.Tests.csproj  # Run transport library tests
dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~ClassName.MethodName"  # Single test
dotnet test --collect:"XPlat Code Coverage"                        # With coverage
```

## Test File Organization

**Location:**
- Separate `test/` directory from `src/` — not co-located with source
- Two test projects mirroring the two source projects

**Naming:**
- Test class suffix: `Tests` (e.g., `TripTimerAppTests`, `AwtrixServiceTests`)
- One file uses `Test` suffix without the `s`: `AwtrixAppMessageTest.cs` — inconsistent, use `Tests`
- Test method names: PascalCase, descriptive with underscores for structure in complex cases:
  - Simple: `GetAlarm`, `ToJsonTextSimple`, `Quantise`
  - Descriptive: `As_Method_Converts_Dictionary_To_TripTimerAppConfig_Successfully`
  - Async: `Deserialize_SuccessfulTripResponse_ShouldDeserializeCorrectly`

**Structure:**
```
test/
├── Test/                                    # Tests for src/api
│   ├── Apps/
│   │   ├── MockClock.cs                     # Test double for IClock
│   │   └── TripTimerAppTests.cs
│   ├── Configs/
│   │   ├── AppConfigTests.cs
│   │   └── ValueMapsTests.cs                # Entirely commented out
│   ├── Domain/
│   │   ├── AwtrixAppMessageTest.cs
│   │   ├── TripSummaryTests.cs              # Factory helper class, not test class
│   │   └── TripTimerAppConfigTests.cs
│   ├── Services/
│   │   ├── AwtrixServiceTests.cs
│   │   └── TimerServiceTests.cs
│   └── Test.csproj
└── transportOpenData.Tests/                 # Tests for src/transportOpenData
    ├── Helpers/
    │   └── TestDataHelper.cs                # Static helper for loading JSON fixtures
    ├── TestData/                            # JSON fixture files (copied to output)
    │   ├── SuccessfulTripResponse.json
    │   ├── ErrorResponse.json
    │   ├── EmptyJourneysResponse.json
    │   └── ComplexTripResponse.json
    ├── TripPlanner/
    │   ├── TripRequestResponseDeserializationTests.cs
    │   └── TripResponseAttributeTests.cs
    └── TransportOpenData.Tests.csproj
```

## Test Structure

**Suite Organization:**
```csharp
// Standard xUnit pattern with factory method for SUT
public class TripTimerAppTests
{
    // Private fields for dependencies (set in factory, not in constructor)
    IClock _clock;
    Mock<ILogger> _mockLog;
    Mock<IAwtrixService> _mockAwtrixService;

    // Factory method creates fresh SUT + mocks per test call
    public TripTimerApp GetSystemUnderTest()
    {
        _clock = new MockClock(DateTimeOffset.Now);
        _mockAwtrixService = new Mock<IAwtrixService>();
        // ... configure mocks ...
        return new TripTimerApp(_mockLog.Object, _clock, ...);
    }

    [Fact]
    public void GetAlarm()
    {
        // Arrange
        var sut = GetSystemUnderTest();
        // Act
        var result = sut.GetAlarmTime(...);
        // Assert
        Assert.Equal(expected, result.SomeProperty);
    }
}
```

**Patterns:**
- Arrange / Act / Assert (AAA) comments used explicitly in most tests
- `GetSystemUnderTest()` factory method pattern (not constructor injection) — allows test-local mock configuration
- `internal` test class used for pure factory helpers: `TripSummaryTests` is `internal class` not a test class
- Constructor-based setup used in `TransportOpenData.Tests`: `JsonSerializerOptions` configured in constructor
- No `[ClassFixture]` or `[CollectionFixture]` detected — each test creates its own state

## Mocking

**Framework:** Moq 4.20.72 (only in `test/Test/` project)

**Patterns:**
```csharp
// Create mock
var mock = new Mock<IAwtrixService>();

// Setup async return
_mockTripPlannerService
    .Setup(x => x.GetNextDepartures(
        It.IsAny<string>(),
        It.IsAny<string>(),
        It.IsAny<DateTime>()))
    .ReturnsAsync(new List<TripSummary> { ... });

// Logging mock setup (verbose pattern required for ILogger)
_mockLog
    .Setup(x => x.Log(
        It.Is<LogLevel>(l => l == LogLevel.Information),
        It.IsAny<EventId>(),
        It.Is<It.IsAnyType>((o, t) => true),
        It.IsAny<Exception>(),
        (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()))
    .Callback((LogLevel logLevel, EventId eventId, object state, Exception ex, Delegate formatter) =>
    {
        Console.WriteLine(state.ToString());
    });

// Use mock object
var sut = new TripTimerApp(_mockLog.Object, _clock, _mockAddress, _mockAwtrixService.Object, ...);
```

**What to Mock:**
- External service interfaces: `IAwtrixService`, `ITripPlannerService`, `ITimerService`
- `ILogger` when testing code that logs (verbose setup required — see pattern above)
- `IClock` — use `MockClock` test double instead of Moq (see below)

**What NOT to Mock:**
- `IClock` — use `MockClock` (`test/Test/Apps/MockClock.cs`) which allows time control
- Value objects and config classes (`AppConfig`, `TripTimerAppConfig`, `AwtrixAddress`) — construct directly
- `AwtrixAppMessage` — construct directly, test its output

## Fixtures and Factories

**Test Doubles:**

`MockClock` at `test/Test/Apps/MockClock.cs` — manual implementation of `IClock`:
```csharp
public class MockClock : IClock
{
    private DateTimeOffset _currentTime;
    public MockClock(DateTimeOffset initialTime) { _currentTime = initialTime; }
    public DateTimeOffset Now => _currentTime;
    public void SetTime(DateTimeOffset newTime) { _currentTime = newTime; }
    public void AdvanceTime(TimeSpan timeSpan) { _currentTime = _currentTime.Add(timeSpan); }
}
```

**Factory Helpers:**

`TripSummaryTests` at `test/Test/Domain/TripSummaryTests.cs` — static factory for `TripSummary`:
```csharp
// Used in tests:
TripSummaryTests.Create(baseTime)
TripSummaryTests.Create(baseTime.AddMinutes(5))
TripSummaryTests.Create(departureTime, arrivalTime)
```

`TestDataHelper` at `test/transportOpenData.Tests/Helpers/TestDataHelper.cs` — static helper for JSON fixtures:
```csharp
// Load and deserialize a fixture file
var response = await TestDataHelper.LoadTestDataAsync<TripRequestResponse>("ComplexTripResponse.json");

// Load raw JSON string
var json = await TestDataHelper.LoadTestDataRawAsync("SuccessfulTripResponse.json");
```

**JSON Test Fixtures:**

Located at `test/transportOpenData.Tests/TestData/` — copied to output with `PreserveNewest`:
- `SuccessfulTripResponse.json` — valid trip response
- `ErrorResponse.json` — API error response
- `EmptyJourneysResponse.json` — zero journeys
- `ComplexTripResponse.json` — multi-leg journey with all optional fields

Fixture JSON serializer options:
```csharp
var options = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};
```

## Coverage

**Requirements:** No enforced coverage threshold detected

**View Coverage:**
```bash
dotnet test --collect:"XPlat Code Coverage"
# Coverage report in TestResults/ directory as coverage.cobertura.xml
```

## Test Types

**Unit Tests:**
- Pure logic tests with no I/O: `AwtrixServiceTests` (static method `Quantize`), `TimerServiceTests` (`FormatClockString`), `AwtrixAppMessageTest` (JSON serialization)
- Config conversion tests: `TripTimerAppConfigTests`, `AppConfigTests`
- App behavior tests with mocked dependencies: `TripTimerAppTests`

**Integration Tests:**
- JSON deserialization against real fixture files: `TripRequestResponseDeserializationTests`, `TripResponseAttributeTests`
- Tests load real JSON from disk and assert against typed objects

**E2E Tests:**
- Not detected — no Playwright, Selenium, or HTTP integration test infrastructure

## Common Patterns

**Theory/InlineData for parameterized tests:**
```csharp
[Theory]
[InlineData(0, 100, "")]    // 0% progress
[InlineData(175, 44, "")]   // 25% progress
[InlineData(300, 0, "")]    // 100% progress
public void GetProgress_ReturnsExpectedProgressValue(int secondsToAlarm, int expectedProgress, string rationale)
{
    // rationale param used as documentation, not asserted
}
```

**Async Testing:**
```csharp
[Fact]
public async Task Deserialize_SuccessfulTripResponse_ShouldDeserializeCorrectly()
{
    // Arrange
    string jsonContent = await File.ReadAllTextAsync("TestData/SuccessfulTripResponse.json");
    // Act
    var response = JsonSerializer.Deserialize<TripRequestResponse>(jsonContent, _jsonOptions);
    // Assert
    Assert.NotNull(response);
}
```

**Static method testing (no SUT instantiation):**
```csharp
[Theory]
[InlineData(0, 0, 4)]
public void Quantise(int rawProgress, int expectedQuantize, int expectedQuantizeBlink)
{
    // Arrange + Act
    var (quantized, quantizedBlink) = AwtrixService.Quantize(rawProgress);
    // Assert
    Assert.Equal(expectedQuantize, quantized);
}
```

**Reflection-based assertions (used in transport tests for optional properties):**
```csharp
var propertiesProperty = info.GetType().GetProperty("Properties");
if (propertiesProperty != null)
{
    var properties = propertiesProperty.GetValue(info) as IDictionary<string, string>;
    // conditional asserts
}
```
Note: This pattern is fragile. Prefer strong typing over reflection in new tests.

## Known Issues

- `test/Test/Configs/ValueMapsTests.cs` is entirely commented out — the tests exist but do not run. These covered `ValueMap.IsMatch()` and `ValueMap.Decorate()` logic.
- `TripSummaryTests.cs` is an `internal` helper class placed in the `Domain` folder — it is not a test class itself. Naming could cause confusion.
- The main test project targets `net9.0` in `Test.csproj` despite the project memory noting an upgrade to `net10.0`. Verify target framework is up to date.

---

*Testing analysis: 2026-02-28*
