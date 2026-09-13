# WS6 Trip Planner Robustness and Timezone Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make trip-timer departures correct and resilient. Queries use Sydney time on any host; malformed, walking, duplicate and cancelled journeys are handled; the HTTP plumbing is singleton-safe with a timeout and cancellation; departures are refreshed during the window; and the file cache and generated model can no longer break a lookup.

**Architecture:**
- `TripPlannerService` becomes a singleton that builds NSwag clients per call over a named `IHttpClientFactory` client.
- Pure helpers carry the logic: `TransportTime` (Sydney conversions), `DepartureMapper` (response → departures) and `TripFileCache` (opt-in cache).
- The generated model gets lenient enums and `RealtimeStatus` through hand-written partials.
- `TripTimerApp` adds a refresh loop on WS4's per-activation token.

**Tech Stack:** .NET 10, ASP.NET Core, NSwag-generated System.Text.Json clients, xUnit, Moq, `Microsoft.Extensions.TimeProvider.Testing`.

**Spec:** `docs/superpowers/specs/2026-09-13-ws6-trip-planner-design.md`

## Global Constraints

- Config compatible: `TRANSPORTOPENDATA__APIKEY`, `TransportOpenData:BaseUrl` (default `https://api.transport.nsw.gov.au/v1/tp`), `AWTRIXSHARP_SETTINGS__DATA_DIRECTORY` and all TripTimer keys keep their meaning. **No new configuration keys.**
- No package changes; target `net10.0`. No `Microsoft.Extensions.Http.Resilience`.
- New constants, verbatim:
  - `TripPlannerService.HttpClientName = "TransportOpenData"`
  - `TripPlannerService.HttpTimeout = TimeSpan.FromSeconds(15)`
  - `TransportTime.TimeZoneId = "Australia/Sydney"`
  - `TripTimerApp.RefreshInterval = TimeSpan.FromMinutes(2)`
  - `TripTimerApp.RetryInterval = TimeSpan.FromSeconds(30)`
  - `TripFileCache.RolloverTolerance = TimeSpan.FromHours(1)`
- Routes and response codes unchanged. Bad controller dates still return 500; WS7 changes that.
- Tests are deterministic: stub `HttpMessageHandler`, `FakeTimeProvider`, scripted delays and TCS gates. No network, no `Task.Delay`/`Thread.Sleep` waits. `WaitAsync(5 s)` and one bounded `SpinWait.SpinUntil` are guards only.
- No step runs the app.
- Do not edit `TripPlannerClient.nswag.cs` (generated); use partials.
- Do not touch Diurnal/Slack/Button/ValueMap files (WS5), `TripTimerAppConfig` (WS7) or `TripSummary` (WS8).
- Commits: explicit `git add <paths>` (never `-A`/`.`); every message ends with:

```
Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt
```

## File map

| File | Change | Task |
|---|---|---|
| `src/transportOpenData/TripPlanner/LenientEnumJson.cs` | new: resolver modifier + lenient nullable enum converter | 1 |
| `src/transportOpenData/TripPlanner/TripPlannerClient.Customizations.cs` | new: `UpdateJsonSerializerSettings` partials, `RealtimeStatus` | 1 |
| `test/transportOpenData.Tests/Helpers/StubHttpMessageHandler.cs` | new | 1 |
| `test/transportOpenData.Tests/TripPlanner/TripClientFixtureTests.cs` | new | 1 |
| `src/api/Services/TripPlanner/TransportTime.cs` | new | 2 |
| `src/api/Interfaces/ITripPlannerService.cs` | `DateTimeOffset` + `CancellationToken` | 2 |
| `src/api/Services/TripPlanner/TripPlannerService.cs` | singleton over factory, BaseUrl, token (T2); mapper (T3); cache (T4) | 2, 3, 4 |
| `src/api/Controllers/TripPlannerController.cs` | Sydney query parsing, tokens | 2 |
| `src/api/Program.cs` | named client + singleton registration | 2 |
| `src/api/Apps/TripTimer/TripTimerApp.cs` | new call signature (T2); refresh loop (T5) | 2, 5 |
| `src/transportOpenData/TransportOpenDataConfig.cs` | BaseUrl default `/v1/tp` | 2 |
| `readme.md` | "Time zones" section | 2 |
| `test/Test/Test.csproj` | link TfNSW fixtures | 2 |
| `test/Test/TripPlanner/TripPlannerTestDoubles.cs` | new | 2 |
| `test/Test/TripPlanner/TransportTimeTests.cs` | new | 2 |
| `test/Test/TripPlanner/TripPlannerServiceTests.cs` | rewrite on stubs (T2); un-skip (T3); cache tests (T4) | 2, 3, 4 |
| `test/Test/TripPlanner/TripPlannerControllerTests.cs` | rewrite on stubs | 2 |
| `test/Test/CompositionRootTests.cs` | trip planner registration test | 2 |
| `test/transportOpenData.Tests/Config/TransportOpenDataConfigTests.cs` | default BaseUrl | 2 |
| `test/Test/Apps/TripTimer/TripTimerAppBoundaryTests.cs`, `test/Test/Apps/TripTimer/TripTimerAppTickTests.cs`, `test/Test/Apps/TripTimerAppTests.cs`, `test/Test/HostedServices/ConductorStartupTests.cs` | Moq setup signature | 2 |
| `src/api/Services/TripPlanner/DepartureMapper.cs` | new | 3 |
| `test/Test/TripPlanner/DepartureMappingTests.cs` | new | 3 |
| `src/api/Services/TripPlanner/TripFileCache.cs` | new | 4 |
| `test/Test/TripPlanner/TripFileCacheTests.cs` | new | 4 |
| `test/Test/Apps/TripTimer/TripTimerAppRefreshTests.cs` | new | 5 |

---

### Task 0: Verify the WS1–WS5 interfaces this plan assumes

No code changes and no commit. Record each result. If a name differs, apply the adaptation rule and use the actual name everywhere in the later tasks.

- [ ] **Step 1: WS4 activation API**

Run: `grep -n "class ScheduledActivation\|CancellationToken Token\|bool IsEnded\|void Complete\|int Number\|ActivationTrigger Trigger" src/api/Apps/ScheduledActivation.cs`
Expected: all six present.

Run: `grep -n "Task OnActivateAsync(ScheduledActivation\|Task OnDeactivateAsync(ScheduledActivation\|ScheduledActivation? CurrentActivation\|internal Task LastRun" src/api/Apps/ScheduledApp.cs`
Expected: all four present.

Run: `grep -n "TimeProvider TimeProvider" src/api/Interfaces/IClock.cs`
Expected: one match (a default interface member).

If WS4 has not landed (the file is missing):
- Tasks 1–4 may proceed.
- In Task 2 Step 8, apply the call-site change to the `GetNextDepartures(...)` call inside `ActivateScheduledWork` instead: `earliestDeparture, cts.Token` in place of `earliestDeparture.LocalDateTime`.
- **Stop before Task 5.**

If `IClock.TimeProvider` is missing, Task 5 uses `TimeProvider.System` in the `RefreshDelay` default.

- [ ] **Step 2: WS4 TripTimerApp shape**

Run: `grep -n "internal void SetDepartures\|internal IReadOnlyList<TripSummary> NextDepartures\|private volatile IReadOnlyList<TripSummary> _nextDepartures\|internal AwtrixAppMessage? BuildMessage(DateTime\|activation.Complete()\|GetNextDepartures" src/api/Apps/TripTimer/TripTimerApp.cs`
Expected:
- `SetDepartures`, `NextDepartures`, `_nextDepartures` and `BuildMessage(DateTime` present
- one `activation.Complete()` inside `ClockTickSecond`
- one `GetNextDepartures(...earliestDeparture.LocalDateTime)` followed by `.WaitAsync(activation.Token)`

- [ ] **Step 3: FireAndLog contract**

Run: `grep -n "protected Task FireAndLog(Func<Task> work, string operation)\|catch (OperationCanceledException)" src/api/Apps/AwtrixApp.cs`
Expected: both present (OCE is logged at Debug).

- [ ] **Step 4: Composition root and planner signature**

Run: `grep -n "services.Configure<TransportOpenDataConfig>\|AddHttpClient<StopfinderClient>\|AddHttpClient<TripClient>\|AddTransient<TripPlannerService>\|AddTransient<ITripPlannerService>" src/api/Program.cs`
Expected: 5 matches. If WS7 somehow landed first (an `AddSettings` method exists), leave its options block alone and replace only the client/service registrations in Task 2 Step 7.

Run: `grep -n "DateTime fromWhen" src/api/Interfaces/ITripPlannerService.cs`
Expected: 2 matches.

Run: `grep -rln "GetNextDepartures(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>())" test/Test`
Expected: `TripTimerAppBoundaryTests.cs`, `TripTimerAppTickTests.cs`, `TripTimerAppTests.cs`, `ConductorStartupTests.cs`. Record any extra file; Task 2 Step 9 updates it too.

- [ ] **Step 5: Baseline**

Run: `dotnet build`
Expected: Build succeeded.

Run: `dotnet test`
Expected: 0 failed. Record the pass count. Two tests are skipped: `GetNextDepartures_NullEstimatedTime_FallsBackToPlannedTime` and the `TripSummary.Factory` one.

---

### Task 1: Lenient generated model, `RealtimeStatus`, real-client fixture tests (CR-38, CR-28 model, CR-40)

**Files:**
- Create: `src/transportOpenData/TripPlanner/LenientEnumJson.cs`
- Create: `src/transportOpenData/TripPlanner/TripPlannerClient.Customizations.cs`
- Create: `test/transportOpenData.Tests/Helpers/StubHttpMessageHandler.cs`
- Test: `test/transportOpenData.Tests/TripPlanner/TripClientFixtureTests.cs`

**Interfaces:**
- Consumes: generated `TripClient(HttpClient)`, `Request2Async(..., CancellationToken)`, `static partial void UpdateJsonSerializerSettings(JsonSerializerOptions)` on each client.
- Produces:
  - `public static class LenientEnumJson { static void Apply(JsonSerializerOptions settings); }`
  - `public sealed class LenientNullableEnumConverter<TEnum> : JsonConverter<TEnum?>`
  - `public ICollection<string>? TripRequestResponseJourneyLeg.RealtimeStatus` (JSON `realtimeStatus`)

- [ ] **Step 1: Write the stub handler and the failing tests**

`test/transportOpenData.Tests/Helpers/StubHttpMessageHandler.cs`:

```csharp
using System.Net;
using System.Text;

namespace TransportOpenData.Tests.Helpers
{
    /// <summary>
    /// Answers every request from a delegate so generated clients run end to end without a network.
    /// </summary>
    public sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        {
            _respond = respond;
        }

        public List<Uri> RequestUris { get; } = new();

        public static StubHttpMessageHandler Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
            new((_, _) => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            }));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (RequestUris)
            {
                RequestUris.Add(request.RequestUri!);
            }

            return _respond(request, cancellationToken);
        }
    }
}
```

`test/transportOpenData.Tests/TripPlanner/TripClientFixtureTests.cs`:

```csharp
using System.Net;
using TransportOpenData.Tests.Helpers;
using TransportOpenData.TripPlanner;
using Xunit;

namespace TransportOpenData.Tests.TripPlanner
{
    /// <summary>
    /// Fixtures pushed through the real generated TripClient and its own serializer settings (CR-40).
    /// TripRequestResponseDeserializationTests use hand-made case-insensitive options that production never uses.
    /// </summary>
    public class TripClientFixtureTests
    {
        private static string Fixture(string name) => File.ReadAllText(Path.Combine("TestData", name));

        private static async Task<TripRequestResponse> RequestAsync(string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            var client = new TripClient(new HttpClient(StubHttpMessageHandler.Json(json, status)))
            {
                BaseUrl = "https://example.test/v1/tp"
            };

            return await client.Request2Async(
                outputFormat: OutputFormat5.RapidJSON,
                coordOutputFormat: CoordOutputFormat4.EPSG4326,
                depArrMacro: DepArrMacro.Dep,
                itdDate: "20250816",
                itdTime: "1530",
                type_origin: Type_origin.Any,
                name_origin: "222316",
                type_destination: Type_destination.Any,
                name_destination: "200070",
                calcNumberOfTrips: 5,
                wheelchair: null,
                excludedMeans: null,
                exclMOT_1: null,
                exclMOT_2: null,
                exclMOT_4: null,
                exclMOT_5: null,
                exclMOT_7: null,
                exclMOT_9: null,
                exclMOT_11: null,
                tfNSWTR: TfNSWTR.True,
                version: null,
                itOptionsActive: null,
                computeMonomodalTripBicycle: null,
                cycleSpeed: null,
                bikeProfSpeed: null,
                maxTimeBicycle: null,
                onlyITBicycle: null,
                useElevationData: null,
                elevFac: null,
                cancellationToken: CancellationToken.None);
        }

        [Fact]
        public async Task ComplexTripResponse_DeserializesThroughTheClient_WithRealtimeStatus()
        {
            var response = await RequestAsync(Fixture("ComplexTripResponse.json"));

            Assert.Equal(8, response.Journeys.Count);
            var firstLeg = response.Journeys.First().Legs.First();
            Assert.Equal(new[] { "MONITORED" }, firstLeg.RealtimeStatus);
            Assert.Equal(100, response.Journeys.ElementAt(1).Legs.First().Transportation.Product.Class); // leading walk (CR-27)
        }

        [Fact]
        public async Task NumericEnumValue_StillMaps()
        {
            var response = await RequestAsync(Fixture("ComplexTripResponse.json"));

            Assert.Equal(TripRequestResponseJourneyLegInterchangeType._100, response.Journeys.First().Legs.First().Interchange.Type);
        }

        [Fact]
        public async Task ErrorBodyWithStatus200_HasErrorAndNoJourneys()
        {
            var response = await RequestAsync(Fixture("ErrorResponse.json"));

            Assert.Equal("The application calling the API has not been authenticated.", response.Error.Message);
            Assert.Null(response.Journeys);
        }

        [Fact]
        public async Task KnownStopType_StillMapsFromItsWireName()
        {
            var response = await RequestAsync(Fixture("SuccessfulTripResponse.json"));

            Assert.Equal(TripRequestResponseJourneyLegStopType.Stop, response.Journeys.First().Legs.First().Origin.Type);
        }

        [Theory]
        [InlineData("\"gisPoint\"")]      // a value TfNSW uses elsewhere (DestinationType) but this enum lacks (CR-38)
        [InlineData("{\"x\": [1, 2]}")]   // an unexpected token shape
        [InlineData("12345")]             // an undefined number
        public async Task UnknownStopType_DoesNotFailTheResponse(string replacement)
        {
            var json = Fixture("SuccessfulTripResponse.json").Replace("\"type\": \"stop\"", "\"type\": " + replacement);

            var response = await RequestAsync(json);

            var leg = response.Journeys.First().Legs.First();
            Assert.Null(leg.Origin.Type);
            Assert.Equal("Central", leg.Origin.DisassembledName); // the rest of the object still deserializes
        }
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test test/transportOpenData.Tests/TransportOpenData.Tests.csproj --filter "FullyQualifiedName~TripClientFixtureTests"`
Expected: build FAILS with `CS1061: 'TripRequestResponseJourneyLeg' does not contain a definition for 'RealtimeStatus'`.

- [ ] **Step 3: Implement**

`src/transportOpenData/TripPlanner/LenientEnumJson.cs`:

```csharp
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace TransportOpenData.TripPlanner
{
    /// <summary>
    /// CR-38: the generated model puts a strict JsonStringEnumConverter attribute on every enum property, so one value
    /// TfNSW adds later (e.g. "gisPoint" on a leg stop) fails the whole response. A property-level [JsonConverter]
    /// beats options.Converters, so this uses a resolver modifier, which runs after attributes are read and can
    /// replace the per-property converter.
    /// </summary>
    public static class LenientEnumJson
    {
        public static void Apply(JsonSerializerOptions settings)
        {
            var resolver = settings.TypeInfoResolver as DefaultJsonTypeInfoResolver ?? new DefaultJsonTypeInfoResolver();
            resolver.Modifiers.Add(UseLenientEnumConverters);
            settings.TypeInfoResolver = resolver;
        }

        private static void UseLenientEnumConverters(JsonTypeInfo typeInfo)
        {
            if (typeInfo.Kind != JsonTypeInfoKind.Object)
            {
                return;
            }

            foreach (var property in typeInfo.Properties)
            {
                var enumType = Nullable.GetUnderlyingType(property.PropertyType);
                if (enumType is { IsEnum: true })
                {
                    property.CustomConverter = (JsonConverter)Activator.CreateInstance(
                        typeof(LenientNullableEnumConverter<>).MakeGenericType(enumType))!;
                }
            }
        }
    }

    /// <summary>
    /// Reads a nullable enum from its [EnumMember] wire name, member name (ignoring case) or defined number.
    /// Anything else becomes null instead of failing the response.
    /// </summary>
    public sealed class LenientNullableEnumConverter<TEnum> : JsonConverter<TEnum?> where TEnum : struct, Enum
    {
        private static readonly Dictionary<string, TEnum> ByWireName = BuildLookup();

        private static readonly Dictionary<TEnum, string> WireNames = ByWireName
            .GroupBy(pair => pair.Value)
            .ToDictionary(group => group.Key, group => group.First().Key);

        private static Dictionary<string, TEnum> BuildLookup()
        {
            var map = new Dictionary<string, TEnum>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var value = (TEnum)field.GetValue(null)!;
                var wireName = field.GetCustomAttribute<EnumMemberAttribute>()?.Value;
                if (!string.IsNullOrEmpty(wireName))
                {
                    map.TryAdd(wireName, value); // first, so Write prefers the wire name
                }

                map.TryAdd(field.Name, value);
            }

            return map;
        }

        public override TEnum? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String when ByWireName.TryGetValue(reader.GetString()!, out var named):
                    return named;
                case JsonTokenType.Number when reader.TryGetInt32(out var number) && Enum.IsDefined(typeof(TEnum), number):
                    return (TEnum)Enum.ToObject(typeof(TEnum), number);
                case JsonTokenType.StartObject:
                case JsonTokenType.StartArray:
                    // TrySkip, not Skip: Skip throws on a non-final buffer during stream deserialisation
                    reader.TrySkip();
                    return null;
                default:
                    return null;
            }
        }

        public override void Write(Utf8JsonWriter writer, TEnum? value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStringValue(WireNames.TryGetValue(value.Value, out var wireName) ? wireName : value.Value.ToString());
        }
    }
}
```

`src/transportOpenData/TripPlanner/TripPlannerClient.Customizations.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TransportOpenData.TripPlanner
{
    // Hand-written partials for the NSwag-generated TripPlannerClient.nswag.cs. Regenerating that file keeps these.

    public partial class TripClient
    {
        static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => LenientEnumJson.Apply(settings);
    }

    public partial class StopfinderClient
    {
        static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => LenientEnumJson.Apply(settings);
    }

    public partial class AddinfoClient
    {
        static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => LenientEnumJson.Apply(settings);
    }

    public partial class CoordClient
    {
        static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => LenientEnumJson.Apply(settings);
    }

    public partial class DmClient
    {
        static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => LenientEnumJson.Apply(settings);
    }

    public partial class TripRequestResponseJourneyLeg
    {
        /// <summary>
        /// Realtime flags for this leg, e.g. ["MONITORED"] in the fixtures. Present in responses but not in the published
        /// schema, so NSwag dropped it (CR-28). The value TfNSW uses for cancellations is not confirmed from repo data;
        /// consumers treat any value containing "CANCEL" as cancelled.
        /// </summary>
        [JsonPropertyName("realtimeStatus")]
        public ICollection<string>? RealtimeStatus { get; set; }
    }
}
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test test/transportOpenData.Tests/TransportOpenData.Tests.csproj --filter "FullyQualifiedName~TripClientFixtureTests"`
Expected: PASS, 7 tests (4 facts + 3 theory cases), 0 failed.

If the compiler reports `CS0759: No defining declaration found for implementing declaration of partial method` for one of the clients, that client has no `UpdateJsonSerializerSettings` declaration. Delete that client's partial and re-run.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test`
Expected: 0 failed. The existing `TripRequestResponseDeserializationTests` still pass (they use their own options).

- [ ] **Step 6: Commit**

```bash
git add src/transportOpenData/TripPlanner/LenientEnumJson.cs src/transportOpenData/TripPlanner/TripPlannerClient.Customizations.cs test/transportOpenData.Tests/Helpers/StubHttpMessageHandler.cs test/transportOpenData.Tests/TripPlanner/TripClientFixtureTests.cs
git commit -m "fix(transport): lenient enums and realtimeStatus on the trip planner model (CR-38, CR-28)

An unknown enum value such as gisPoint on a leg stop no longer fails the
whole trip response: a resolver modifier swaps the generated strict
converters for one that maps wire names and defined numbers and otherwise
yields null. Legs expose realtimeStatus through a hand-written partial.
Fixtures are now also tested through the real TripClient over a stub
handler (CR-40).

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

### Task 2: Singleton service over IHttpClientFactory, BaseUrl, cancellation, Sydney time (CR-29, CR-36, CR-26, CR-40)

**Files:**
- Create: `src/api/Services/TripPlanner/TransportTime.cs`
- Modify: `src/api/Interfaces/ITripPlannerService.cs` (full replace)
- Modify: `src/api/Services/TripPlanner/TripPlannerService.cs` (full replace)
- Modify: `src/api/Controllers/TripPlannerController.cs` (three actions)
- Modify: `src/api/Program.cs` (trip planner registrations)
- Modify: `src/api/Apps/TripTimer/TripTimerApp.cs` (one call)
- Modify: `src/transportOpenData/TransportOpenDataConfig.cs` (BaseUrl default)
- Modify: `readme.md` (new "Time zones" section)
- Modify: `test/Test/Test.csproj` (fixture link)
- Create: `test/Test/TripPlanner/TripPlannerTestDoubles.cs`
- Create: `test/Test/TripPlanner/TransportTimeTests.cs`
- Modify: `test/Test/TripPlanner/TripPlannerServiceTests.cs` (full replace)
- Modify: `test/Test/TripPlanner/TripPlannerControllerTests.cs` (full replace)
- Modify: `test/Test/CompositionRootTests.cs` (one test)
- Modify: `test/transportOpenData.Tests/Config/TransportOpenDataConfigTests.cs` (one assertion)
- Modify: `test/Test/Apps/TripTimer/TripTimerAppBoundaryTests.cs`, `test/Test/Apps/TripTimer/TripTimerAppTickTests.cs`, `test/Test/Apps/TripTimerAppTests.cs`, `test/Test/HostedServices/ConductorStartupTests.cs` (Moq setups)

**Interfaces:**
- Consumes (Task 1): lenient client deserialisation.
- Produces:
  - `ITripPlannerService`:
    - `Task<StopFinderResponse> FindStops(string query, CancellationToken cancellationToken = default)`
    - `Task<List<TripSummary>> GetNextDepartures(string originStopId, string destinationStopId, DateTimeOffset fromWhen, CancellationToken cancellationToken = default)`
    - `Task<TripRequestResponse> GetTrips(string originStopId, string destinationStopId, DateTimeOffset fromWhen, CancellationToken cancellationToken = default)`
  - `TripPlannerService(IHttpClientFactory httpClientFactory, IOptions<TransportOpenDataConfig> config, ILogger<TripPlannerService> logger)`; `public const string HttpClientName`; `public static readonly TimeSpan HttpTimeout`
  - `public static class TransportTime`:
    - `TimeZoneId`, `Zone`
    - `ToTransportZone(DateTimeOffset)`
    - `ToQuery(DateTimeOffset) → (string itdDate, string itdTime)`
    - `FromTransportWallClock(DateTime)`
    - `TryParseQuery(string?, out DateTimeOffset)`
    - `TryParseApiTime(string?, out DateTimeOffset)`
  - Test doubles (namespace `Test.TripPlanner`):
    - `StubHttpMessageHandler` (`Json`, `RequestUris`)
    - `StubHttpClientFactory` (`CreatedNames`)
    - `TripPlannerTestData`: `BaseUrl`, `Fixture`, `ToJson`, `CreateService`, `Stop`, `Leg`, `Journey`, `Response`

- [ ] **Step 1: Link the fixtures and add the test doubles**

In `test/Test/Test.csproj`, add this `ItemGroup` before `</Project>`:

```xml
  <ItemGroup>
    <None Include="..\transportOpenData.Tests\TestData\*.json" Link="TripPlanner\TestData\%(Filename)%(Extension)">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
```

`test/Test/TripPlanner/TripPlannerTestDoubles.cs`:

```csharp
using System.Net;
using System.Text;
using System.Text.Json;
using AwtrixSharpWeb.Services.TripPlanner;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TransportOpenData;
using TransportOpenData.TripPlanner;

namespace Test.TripPlanner
{
    /// <summary>
    /// Answers every request from a delegate, so the real generated clients and their serializer settings run without a network.
    /// </summary>
    internal sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        {
            _respond = respond;
        }

        public List<Uri> RequestUris { get; } = new();

        public static StubHttpMessageHandler Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
            new((_, _) => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            }));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (RequestUris)
            {
                RequestUris.Add(request.RequestUri!);
            }

            return _respond(request, cancellationToken);
        }
    }

    internal sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public List<string> CreatedNames { get; } = new();

        public HttpClient CreateClient(string name)
        {
            lock (CreatedNames)
            {
                CreatedNames.Add(name);
            }

            return new HttpClient(_handler, disposeHandler: false);
        }
    }

    internal static class TripPlannerTestData
    {
        public const string BaseUrl = "https://example.test/v1/tp";

        /// <summary>TfNSW fixtures linked from test/transportOpenData.Tests/TestData</summary>
        public static string Fixture(string fileName) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TripPlanner", "TestData", fileName));

        /// <summary>Serialises with default options; the generated [JsonPropertyName] attributes give the wire names</summary>
        public static string ToJson(TripRequestResponse response) => JsonSerializer.Serialize(response);

        public static TripPlannerService CreateService(HttpMessageHandler handler, ILogger<TripPlannerService>? logger = null, string baseUrl = BaseUrl) =>
            new(new StubHttpClientFactory(handler),
                Options.Create(new TransportOpenDataConfig { ApiKey = "test-key", BaseUrl = baseUrl }),
                logger ?? NullLogger<TripPlannerService>.Instance);

        public static TripRequestResponseJourneyLegStop Stop(string? estimated, string? planned, string name) => new()
        {
            ArrivalTimeEstimated = estimated,
            ArrivalTimePlanned = planned,
            DepartureTimeEstimated = estimated,
            DepartureTimePlanned = planned,
            DisassembledName = name
        };

        public static TripRequestResponseJourneyLeg Leg(
            TripRequestResponseJourneyLegStop origin,
            TripRequestResponseJourneyLegStop destination,
            int? productClass = null,
            params string[] realtimeStatus) => new()
        {
            Origin = origin,
            Destination = destination,
            Transportation = productClass is null ? null : new TripTransportation { Product = new RouteProduct { Class = productClass } },
            RealtimeStatus = realtimeStatus.Length == 0 ? null : realtimeStatus
        };

        public static TripRequestResponseJourney Journey(params TripRequestResponseJourneyLeg[] legs) => new() { Legs = legs.ToList() };

        public static TripRequestResponse Response(params TripRequestResponseJourney?[] journeys) => new() { Journeys = journeys.ToList()! };
    }
}
```

- [ ] **Step 2: Write the failing tests**

`test/Test/TripPlanner/TransportTimeTests.cs`:

```csharp
using System.Globalization;
using AwtrixSharpWeb.Services.TripPlanner;

namespace Test.TripPlanner
{
    /// <summary>
    /// CR-26: Transport NSW speaks Sydney wall-clock time, whatever the host's TZ.
    /// </summary>
    public class TransportTimeTests
    {
        [Theory]
        [InlineData("2025-08-15T20:22:00Z", "20250816", "0622")]      // UTC host at 06:22 AEST: the CR-26 scenario
        [InlineData("2025-08-16T06:22:00+10:00", "20250816", "0622")]
        [InlineData("2025-08-16T01:52:00+05:30", "20250816", "0622")] // any offset, same instant
        [InlineData("2024-10-05T16:30:00Z", "20241006", "0330")]      // just after DST starts (AEDT)
        [InlineData("2024-04-06T15:30:00Z", "20240407", "0230")]      // 02:30 AEDT, before clocks go back
        [InlineData("2024-04-06T16:30:00Z", "20240407", "0230")]      // 02:30 AEST, after clocks go back
        public void ToQuery_FormatsSydneyWallClock(string instant, string itdDate, string itdTime)
        {
            var query = TransportTime.ToQuery(DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture));

            Assert.Equal((itdDate, itdTime), query);
        }

        [Fact]
        public void ToQuery_UsesGregorianDigits_WhateverTheCurrentCulture()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("th-TH"); // Buddhist calendar: the year would print as 2568

                Assert.Equal(("20250816", "0622"), TransportTime.ToQuery(DateTimeOffset.Parse("2025-08-15T20:22:00Z", CultureInfo.InvariantCulture)));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Theory]
        [InlineData("2025-09-01T06:00:00", "2025-09-01T06:00:00+10:00")] // no offset: Sydney wall clock (AEST)
        [InlineData("2025-09-01 06:41", "2025-09-01T06:41:00+10:00")]
        [InlineData("2025-01-02T06:00:00", "2025-01-02T06:00:00+11:00")] // AEDT
        [InlineData("2025-09-01T06:00:00Z", "2025-09-01T06:00:00+00:00")] // explicit instant kept
        [InlineData("2025-09-01T06:00:00+08:00", "2025-09-01T06:00:00+08:00")]
        public void TryParseQuery_OffsetlessIsSydney_OtherwiseTheGivenInstant(string value, string expected)
        {
            Assert.True(TransportTime.TryParseQuery(value, out var parsed));

            var expectedInstant = DateTimeOffset.Parse(expected, CultureInfo.InvariantCulture);
            Assert.Equal(expectedInstant, parsed);
            if (!value.EndsWith("Z") && !value.Contains('+'))
            {
                Assert.Equal(expectedInstant.Offset, parsed.Offset);
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not-a-date")]
        public void TryParseQuery_RejectsGarbage(string? value)
        {
            Assert.False(TransportTime.TryParseQuery(value, out _));
        }

        [Fact]
        public void TryParseApiTime_ReturnsTheInstantWithTheSydneyOffset()
        {
            Assert.True(TransportTime.TryParseApiTime("2025-08-16T05:37:54Z", out var parsed));

            Assert.Equal(DateTimeOffset.Parse("2025-08-16T15:37:54+10:00", CultureInfo.InvariantCulture), parsed);
            Assert.Equal(TimeSpan.FromHours(10), parsed.Offset);
        }

        [Theory]
        [InlineData(null)]
        [InlineData(" ")]
        [InlineData("soon")]
        public void TryParseApiTime_RejectsMissingOrGarbage(string? value)
        {
            Assert.False(TransportTime.TryParseApiTime(value, out _));
        }
    }
}
```

Replace the whole of `test/Test/TripPlanner/TripPlannerServiceTests.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using AwtrixSharpWeb.Services.TripPlanner;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TransportOpenData;
using TransportOpenData.TripPlanner;
using static Test.TripPlanner.TripPlannerTestData;

namespace Test.TripPlanner
{
    /// <summary>
    /// TripPlannerService over the real generated clients and a stub HttpMessageHandler (CR-40): production serializer
    /// settings are exercised and no network is used. Tests that set AWTRIXSHARP_SETTINGS__DATA_DIRECTORY live in
    /// this class so xUnit runs them serially.
    /// </summary>
    public class TripPlannerServiceTests
    {
        private const string DataDirectoryVariable = "AWTRIXSHARP_SETTINGS__DATA_DIRECTORY";
        private static readonly DateTimeOffset AnyTime = new(2024, 6, 1, 9, 0, 0, TimeSpan.FromHours(10));

        private static (TripPlannerService Service, StubHttpMessageHandler Handler) ServiceReturning(TripRequestResponse response)
        {
            var handler = StubHttpMessageHandler.Json(ToJson(response));
            return (CreateService(handler), handler);
        }

        [Fact]
        public async Task FindStops_UsesTheConfiguredBaseUrl_AndReturnsTheResponse()
        {
            var handler = StubHttpMessageHandler.Json("{\"version\":\"10.5\"}");
            var sut = CreateService(handler);

            var result = await sut.FindStops("Central");

            Assert.Equal("10.5", result.Version);
            Assert.StartsWith(BaseUrl + "/stop_finder?", Assert.Single(handler.RequestUris).ToString());
        }

        [Fact]
        public async Task GetTrips_BlankBaseUrl_UsesTheGeneratedDefault()
        {
            var handler = StubHttpMessageHandler.Json("{\"journeys\":[]}");
            var sut = CreateService(handler, baseUrl: "");

            await sut.GetTrips("200080", "200060", AnyTime);

            Assert.StartsWith("https://api.transport.nsw.gov.au/v1/tp/trip?", Assert.Single(handler.RequestUris).ToString());
        }

        [Fact]
        public async Task GetTrips_AtSixTwentyTwoSydneyFromAUtcHost_QueriesTheSydneyDateAndTime()
        {
            // CR-26: a UTC container used to ask for 20:22 on the previous date
            var (sut, handler) = ServiceReturning(Response());

            await sut.GetTrips("200080", "200060", DateTimeOffset.Parse("2025-08-15T20:22:00Z", CultureInfo.InvariantCulture));

            var uri = Assert.Single(handler.RequestUris).ToString();
            Assert.Contains("itdDate=20250816", uri);
            Assert.Contains("itdTime=0622", uri);
        }

        [Fact]
        public async Task EachCall_CreatesAFreshNamedClient()
        {
            // CR-29: nothing captures an HttpClient for the life of the process
            var factory = new StubHttpClientFactory(StubHttpMessageHandler.Json("{\"journeys\":[]}"));
            var sut = new TripPlannerService(factory, Options.Create(new TransportOpenDataConfig { BaseUrl = BaseUrl }), NullLogger<TripPlannerService>.Instance);

            await sut.GetTrips("200080", "200060", AnyTime);
            await sut.GetTrips("200080", "200060", AnyTime);

            Assert.Equal(new[] { TripPlannerService.HttpClientName, TripPlannerService.HttpClientName }, factory.CreatedNames);
        }

        [Fact]
        public async Task GetNextDepartures_CancellingTheToken_CancelsTheRequest()
        {
            var handler = new StubHttpMessageHandler((_, token) =>
            {
                var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
                token.Register(() => pending.TrySetCanceled(token));
                return pending.Task;
            });
            var sut = CreateService(handler);
            using var cts = new CancellationTokenSource();

            var call = sut.GetNextDepartures("200080", "200060", AnyTime, cts.Token);
            Assert.False(call.IsCompleted);
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(5)));
        }

        [Fact]
        public async Task GetNextDepartures_UsesEstimatedTimesAndDisassembledNames()
        {
            var (sut, _) = ServiceReturning(Response(Journey(Leg(
                Stop("2024-06-01T00:05:00Z", "2024-06-01T00:07:00Z", "Central"),
                Stop("2024-06-01T00:35:00Z", "2024-06-01T00:37:00Z", "Circular Quay")))));

            var result = await sut.GetNextDepartures("200080", "200060", AnyTime);

            var summary = Assert.Single(result);
            Assert.Equal(DateTimeOffset.Parse("2024-06-01T00:05:00Z", CultureInfo.InvariantCulture), summary.Origin.Time);
            Assert.Equal("Central", summary.Origin.Place);
            Assert.Equal(DateTimeOffset.Parse("2024-06-01T00:35:00Z", CultureInfo.InvariantCulture), summary.Destination.Time);
            Assert.Equal("Circular Quay", summary.Destination.Place);
        }

        [Fact]
        public async Task GetNextDepartures_ReturnsTimesWithTheSydneyOffset()
        {
            var (sut, _) = ServiceReturning(Response(Journey(Leg(
                Stop("2024-06-01T00:05:00Z", null, "Central"),
                Stop("2024-06-01T00:35:00Z", null, "Circular Quay")))));

            var summary = Assert.Single(await sut.GetNextDepartures("200080", "200060", AnyTime));

            Assert.Equal(TimeSpan.FromHours(10), summary.Origin.Time.Offset);
            Assert.Equal(10, summary.Origin.Time.Hour);
        }

        [Fact]
        public async Task GetNextDepartures_MultiLegJourney_UsesFirstLegOriginAndLastLegDestination()
        {
            var (sut, _) = ServiceReturning(Response(Journey(
                Leg(Stop("2024-06-01T00:00:00Z", "2024-06-01T00:00:00Z", "Origin Stop"), Stop("2024-06-01T00:10:00Z", "2024-06-01T00:10:00Z", "Interchange Stop")),
                Leg(Stop("2024-06-01T00:15:00Z", "2024-06-01T00:15:00Z", "Interchange Stop"), Stop("2024-06-01T00:45:00Z", "2024-06-01T00:45:00Z", "Final Stop")))));

            var summary = Assert.Single(await sut.GetNextDepartures("200080", "200060", AnyTime));

            Assert.Equal("Origin Stop", summary.Origin.Place);
            Assert.Equal(DateTimeOffset.Parse("2024-06-01T00:00:00Z", CultureInfo.InvariantCulture), summary.Origin.Time);
            Assert.Equal("Final Stop", summary.Destination.Place);
            Assert.Equal(DateTimeOffset.Parse("2024-06-01T00:45:00Z", CultureInfo.InvariantCulture), summary.Destination.Time);
        }

        [Fact]
        public async Task GetNextDepartures_MultipleJourneys_PreservesApiOrder()
        {
            var (sut, _) = ServiceReturning(Response(
                Journey(Leg(Stop("2024-06-01T00:30:00Z", null, "Third"), Stop("2024-06-01T01:00:00Z", null, "ThirdDest"))),
                Journey(Leg(Stop("2024-06-01T00:00:00Z", null, "First"), Stop("2024-06-01T00:30:00Z", null, "FirstDest"))),
                Journey(Leg(Stop("2024-06-01T00:15:00Z", null, "Second"), Stop("2024-06-01T00:45:00Z", null, "SecondDest")))));

            var result = await sut.GetNextDepartures("200080", "200060", AnyTime);

            Assert.Equal(new[] { "Third", "First", "Second" }, result.Select(r => r.Origin.Place));
        }

        [Fact]
        public async Task GetNextDepartures_EmptyJourneys_ReturnsEmptyList()
        {
            var (sut, _) = ServiceReturning(Response());

            Assert.Empty(await sut.GetNextDepartures("200080", "200060", AnyTime));
        }

        [Theory]
        [InlineData("2024-10-06T01:30:00+10:00")] // just before Sydney DST starts (AEST)
        [InlineData("2024-10-06T03:30:00+11:00")] // just after Sydney DST starts (AEDT)
        [InlineData("2024-04-07T02:30:00+11:00")] // just before Sydney DST ends
        [InlineData("2024-04-07T02:30:00+10:00")] // just after Sydney DST ends (clocks repeat 2-3am)
        public async Task GetNextDepartures_PreservesInstantAndSydneyOffset_AcrossDstTransitions(string estimated)
        {
            var (sut, _) = ServiceReturning(Response(Journey(Leg(Stop(estimated, estimated, "Origin"), Stop(estimated, estimated, "Destination")))));

            var summary = Assert.Single(await sut.GetNextDepartures("200080", "200060", AnyTime));

            var expected = DateTimeOffset.Parse(estimated, CultureInfo.InvariantCulture);
            Assert.Equal(expected, summary.Origin.Time);
            Assert.Equal(expected.Offset, summary.Origin.Time.Offset);
        }

        [Fact(Skip = "Known bug (fixed in WS6 Task 3): no fallback from a null estimated time to the planned time.")]
        public async Task GetNextDepartures_NullEstimatedTime_FallsBackToPlannedTime()
        {
            const string planned = "2024-06-01T00:05:00Z";
            var (sut, _) = ServiceReturning(Response(Journey(Leg(
                new TripRequestResponseJourneyLegStop { DepartureTimeEstimated = null, DepartureTimePlanned = planned, DisassembledName = "Central" },
                new TripRequestResponseJourneyLegStop { ArrivalTimeEstimated = null, ArrivalTimePlanned = planned, DisassembledName = "Town Hall" }))));

            var summary = Assert.Single(await sut.GetNextDepartures("200080", "200060", AnyTime));

            Assert.Equal(DateTimeOffset.Parse(planned, CultureInfo.InvariantCulture), summary.Origin.Time);
        }

        [Fact]
        public async Task GetNextDepartures_UsesFileCache_WhenPresent_AndDoesNotCallTheApi()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "awtrixsharp-tests-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            var previousValue = Environment.GetEnvironmentVariable(DataDirectoryVariable);

            try
            {
                var fromWhen = new DateTimeOffset(2025, 1, 1, 8, 0, 0, TimeSpan.FromHours(11)); // 08:00 Sydney (AEDT)
                var now = DateTimeOffset.Now;
                var cachedTrips = new List<TripSummary>
                {
                    new()
                    {
                        Origin = new TimePlace { Time = new DateTimeOffset(2000, 1, 1, 6, 30, 15, now.Offset), Place = "CachedOrigin" },
                        Destination = new TimePlace { Time = new DateTimeOffset(2000, 1, 1, 7, 0, 45, now.Offset), Place = "CachedDestination" }
                    }
                };
                File.WriteAllText(Path.Combine(tempDir, "trip_originA_destB_08.json"), JsonSerializer.Serialize(cachedTrips));
                Environment.SetEnvironmentVariable(DataDirectoryVariable, tempDir);

                var handler = StubHttpMessageHandler.Json("{\"journeys\":[]}");
                var sut = CreateService(handler);

                var result = await sut.GetNextDepartures("originA", "destB", fromWhen);

                var summary = Assert.Single(result);
                Assert.Equal("CachedOrigin", summary.Origin.Place);
                Assert.Equal(now.Day, summary.Origin.Time.Day); // re-dating is corrected in Task 4
                Assert.Equal(6, summary.Origin.Time.Hour);
                Assert.Equal(30, summary.Origin.Time.Minute);
                Assert.Equal(15, summary.Origin.Time.Second);
                Assert.Empty(handler.RequestUris);
            }
            finally
            {
                Environment.SetEnvironmentVariable(DataDirectoryVariable, previousValue);
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task GetNextDepartures_NoDataDirectory_CallsTheApi()
        {
            var previousValue = Environment.GetEnvironmentVariable(DataDirectoryVariable);
            try
            {
                Environment.SetEnvironmentVariable(DataDirectoryVariable, null);
                var (sut, handler) = ServiceReturning(Response(Journey(Leg(
                    Stop("2024-06-01T00:05:00Z", null, "Central"),
                    Stop("2024-06-01T00:35:00Z", null, "Circular Quay")))));

                var result = await sut.GetNextDepartures("200080", "200060", AnyTime);

                Assert.Single(result);
                Assert.Single(handler.RequestUris);
            }
            finally
            {
                Environment.SetEnvironmentVariable(DataDirectoryVariable, previousValue);
            }
        }
    }
}
```

Replace the whole of `test/Test/TripPlanner/TripPlannerControllerTests.cs`:

```csharp
using System.Net;
using AwtrixSharpWeb.Controllers;
using AwtrixSharpWeb.Services.TripPlanner;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using TransportOpenData.TripPlanner;
using static Test.TripPlanner.TripPlannerTestData;

namespace Test.TripPlanner
{
    /// <summary>
    /// TripPlannerController over a real TripPlannerService whose generated clients talk to a stub handler.
    /// </summary>
    public class TripPlannerControllerTests
    {
        private static TripPlannerController Create(HttpMessageHandler handler) =>
            new(CreateService(handler), NullLogger<TripPlannerController>.Instance);

        private static StubHttpMessageHandler Throwing(Exception exception) =>
            new((_, _) => Task.FromException<HttpResponseMessage>(exception));

        [Fact]
        public async Task FindStops_ReturnsOk_WithServiceResult()
        {
            var sut = Create(StubHttpMessageHandler.Json("{\"version\":\"10.5\"}"));

            var result = await sut.FindStops("Central");

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal("10.5", Assert.IsType<StopFinderResponse>(okResult.Value).Version);
        }

        [Fact]
        public async Task FindStops_ServiceThrows_Returns500()
        {
            var sut = Create(Throwing(new HttpRequestException("network error")));

            var result = await sut.FindStops("Central");

            Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);
        }

        [Fact]
        public async Task GetDepartures_ReturnsOk_WithTripSummaries()
        {
            var sut = Create(StubHttpMessageHandler.Json(ToJson(Response(Journey(Leg(
                Stop("2025-09-01T06:41:00+10:00", null, "Central"),
                Stop("2025-09-01T07:10:00+10:00", null, "Town Hall")))))));

            var result = await sut.GetDepartures("200080", "200060", "2025-09-01T06:00:00");

            var okResult = Assert.IsType<OkObjectResult>(result);
            var trip = Assert.Single(Assert.IsAssignableFrom<List<TripSummary>>(okResult.Value));
            Assert.Equal("Central", trip.Origin.Place);
        }

        [Fact]
        public async Task GetDepartures_OffsetlessFromDateTime_IsSydneyWallClock()
        {
            // CR-26: used to be parsed as host-local time
            var handler = StubHttpMessageHandler.Json(ToJson(Response()));
            var sut = Create(handler);

            await sut.GetDepartures("200080", "200060", "2025-09-01T06:00:00");

            var uri = Assert.Single(handler.RequestUris).ToString();
            Assert.Contains("itdDate=20250901", uri);
            Assert.Contains("itdTime=0600", uri);
        }

        [Fact]
        public async Task GetDepartures_InvalidDateTime_Returns500()
        {
            var sut = Create(StubHttpMessageHandler.Json("{}"));

            var result = await sut.GetDepartures("200080", "200060", "not-a-date");

            Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);
        }

        [Fact]
        public async Task GetDepartures_ApiReturns503_Returns500()
        {
            var sut = Create(StubHttpMessageHandler.Json("{}", HttpStatusCode.ServiceUnavailable));

            var result = await sut.GetDepartures("200080", "200060", "2025-09-01T06:00:00");

            Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);
        }

        [Fact]
        public async Task GetTrip_ReturnsOk_WithRawTripResponse()
        {
            var sut = Create(StubHttpMessageHandler.Json("{\"version\":\"10.5\",\"journeys\":[]}"));

            var result = await sut.GetTrip("200080", "200060", "2025-09-01T06:00:00");

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal("10.5", Assert.IsType<TripRequestResponse>(okResult.Value).Version);
        }

        [Fact]
        public async Task GetTrip_InvalidDateTime_Returns500()
        {
            var sut = Create(StubHttpMessageHandler.Json("{}"));

            var result = await sut.GetTrip("200080", "200060", "not-a-date");

            Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);
        }
    }
}
```

In `test/Test/CompositionRootTests.cs`:
- add `using AwtrixSharpWeb.Services.TripPlanner;` at the top
- add this test inside the class, after `HttpPublisherNamedClient_HasFiveSecondTimeout`:

```csharp
        [Fact]
        public void TripPlanner_IsASingleton_OverANamedClientWithTimeoutAndApiKeyHeader()
        {
            // CR-29: no transient service holding typed clients captured by the singleton Conductor
            using var provider = BuildProvider();

            Assert.Same(provider.GetRequiredService<TripPlannerService>(), provider.GetRequiredService<ITripPlannerService>());
            Assert.Same(provider.GetRequiredService<ITripPlannerService>(), provider.GetRequiredService<ITripPlannerService>());

            var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(TripPlannerService.HttpClientName);
            Assert.Equal(TimeSpan.FromSeconds(15), client.Timeout);
            Assert.StartsWith("apikey", Assert.Single(client.DefaultRequestHeaders.GetValues("Authorization")));
        }
```

In `test/transportOpenData.Tests/Config/TransportOpenDataConfigTests.cs`, change the assertion in `DefaultConstructor_SetsExpectedDefaultBaseUrl` to:

```csharp
            Assert.Equal("https://api.transport.nsw.gov.au/v1/tp", sut.BaseUrl);
```

- [ ] **Step 3: Run the tests and confirm they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.TripPlanner|FullyQualifiedName~Test.CompositionRootTests"`
Expected: build FAILS with `CS0103: The name 'TransportTime' does not exist` and `CS1503` on the `TripPlannerService` constructor arguments.

- [ ] **Step 4: Implement `TransportTime` and the interface**

`src/api/Services/TripPlanner/TransportTime.cs`:

```csharp
using System.Globalization;

namespace AwtrixSharpWeb.Services.TripPlanner
{
    /// <summary>
    /// Transport NSW reads and returns wall-clock times in Sydney, whatever the host's TZ (CR-26).
    /// Cron and Diurnal schedules stay host-local; see readme "Time zones".
    /// </summary>
    public static class TransportTime
    {
        public const string TimeZoneId = "Australia/Sydney";
        private const string WindowsTimeZoneId = "AUS Eastern Standard Time";

        public static TimeZoneInfo Zone { get; } = FindZone();

        private static TimeZoneInfo FindZone()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
                // Windows without ICU (invariant globalization) cannot map IANA ids
                return TimeZoneInfo.FindSystemTimeZoneById(WindowsTimeZoneId);
            }
        }

        /// <summary>The same instant, with Sydney's offset</summary>
        public static DateTimeOffset ToTransportZone(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Zone);

        /// <summary>The Trip Planner itdDate (yyyyMMdd) and itdTime (HHmm) for an instant</summary>
        public static (string itdDate, string itdTime) ToQuery(DateTimeOffset instant)
        {
            var sydney = ToTransportZone(instant);
            return (sydney.ToString("yyyyMMdd", CultureInfo.InvariantCulture), sydney.ToString("HHmm", CultureInfo.InvariantCulture));
        }

        /// <summary>A Sydney wall-clock time as an instant</summary>
        public static DateTimeOffset FromTransportWallClock(DateTime wallClock)
        {
            var unspecified = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
            return new DateTimeOffset(unspecified, Zone.GetUtcOffset(unspecified));
        }

        /// <summary>
        /// Parse a user-supplied date/time. Without an offset it is Sydney wall clock; with an offset or Z it is that instant.
        /// </summary>
        public static bool TryParseQuery(string? value, out DateTimeOffset result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(value)
                || !DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            {
                return false;
            }

            if (parsed.Kind == DateTimeKind.Unspecified)
            {
                result = FromTransportWallClock(parsed);
                return true;
            }

            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out result);
        }

        /// <summary>Parse a Trip Planner timestamp into an instant with Sydney's offset; false when missing or unparsable</summary>
        public static bool TryParseApiTime(string? value, out DateTimeOffset result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(value)
                || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                return false;
            }

            result = ToTransportZone(parsed);
            return true;
        }
    }
}
```

Replace the whole of `src/api/Interfaces/ITripPlannerService.cs`:

```csharp
using AwtrixSharpWeb.Services.TripPlanner;
using TransportOpenData.TripPlanner;

namespace AwtrixSharpWeb.Interfaces
{
    public interface ITripPlannerService
    {
        Task<StopFinderResponse> FindStops(string query, CancellationToken cancellationToken = default);

        /// <summary>
        /// Departures after <paramref name="fromWhen"/> (an instant; converted to Sydney time for the API).
        /// HTTP failures throw; malformed journeys are skipped.
        /// </summary>
        Task<List<TripSummary>> GetNextDepartures(string originStopId, string destinationStopId, DateTimeOffset fromWhen, CancellationToken cancellationToken = default);

        Task<TripRequestResponse> GetTrips(string originStopId, string destinationStopId, DateTimeOffset fromWhen, CancellationToken cancellationToken = default);
    }
}
```

- [ ] **Step 5: Implement the service (full replace of `src/api/Services/TripPlanner/TripPlannerService.cs`)**

```csharp
using System.Globalization;
using System.Text.Json;
using AwtrixSharpWeb.Interfaces;
using Microsoft.Extensions.Options;
using TransportOpenData;
using TransportOpenData.TripPlanner;

namespace AwtrixSharpWeb.Services.TripPlanner
{
    /// <summary>
    /// Transport NSW Trip Planner access. A singleton: every call builds a short-lived NSwag client over the named
    /// IHttpClientFactory client, so pooled handlers rotate (DNS changes are picked up), requests time out after
    /// <see cref="HttpTimeout"/>, and callers can cancel (CR-29). BaseUrl comes from TransportOpenDataConfig (CR-36).
    /// Times on the wire are Sydney wall clock (CR-26).
    /// </summary>
    public class TripPlannerService : ITripPlannerService
    {
        public const string HttpClientName = "TransportOpenData";
        public static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(15);
        internal const int TripsPerQuery = 5;

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IOptions<TransportOpenDataConfig> _config;
        private readonly ILogger<TripPlannerService> _logger;

        public TripPlannerService(
            IHttpClientFactory httpClientFactory,
            IOptions<TransportOpenDataConfig> config,
            ILogger<TripPlannerService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
            _logger = logger;
        }

        public async Task<StopFinderResponse> FindStops(string query, CancellationToken cancellationToken = default)
        {
            using var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            var client = new StopfinderClient(httpClient);
            ApplyBaseUrl(url => client.BaseUrl = url);

            return await client.RequestAsync(
                OutputFormat4.RapidJSON,
                Type_sf.Any,
                query,
                CoordOutputFormat3.EPSG4326,
                null,
                null,
                cancellationToken);
        }

        public async Task<TripRequestResponse> GetTrips(string originStopId, string destinationStopId, DateTimeOffset fromWhen, CancellationToken cancellationToken = default)
        {
            var (itdDate, itdTime) = TransportTime.ToQuery(fromWhen);
            _logger.LogInformation("Getting trips from {Origin} to {Destination} departing after {ItdDate} {ItdTime} ({TimeZone})",
                originStopId, destinationStopId, itdDate, itdTime, TransportTime.TimeZoneId);

            using var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            var client = new TripClient(httpClient);
            ApplyBaseUrl(url => client.BaseUrl = url);

            return await client.Request2Async(
                outputFormat: OutputFormat5.RapidJSON,
                coordOutputFormat: CoordOutputFormat4.EPSG4326,
                depArrMacro: DepArrMacro.Dep, // Departing after the specified time
                itdDate: itdDate,
                itdTime: itdTime,
                type_origin: Type_origin.Any,
                name_origin: originStopId,
                type_destination: Type_destination.Any,
                name_destination: destinationStopId,
                calcNumberOfTrips: TripsPerQuery,
                wheelchair: null,
                excludedMeans: null,
                exclMOT_1: null,
                exclMOT_2: null,
                exclMOT_4: null,
                exclMOT_5: null,
                exclMOT_7: null,
                exclMOT_9: null,
                exclMOT_11: null,
                tfNSWTR: TfNSWTR.True, // Enable real-time data
                version: null,
                itOptionsActive: null,
                computeMonomodalTripBicycle: null,
                cycleSpeed: null,
                bikeProfSpeed: null,
                maxTimeBicycle: null,
                onlyITBicycle: null,
                useElevationData: null,
                elevFac: null,
                cancellationToken: cancellationToken);
        }

        public async Task<List<TripSummary>> GetNextDepartures(string originStopId, string destinationStopId, DateTimeOffset fromWhen, CancellationToken cancellationToken = default)
        {
            var cachedDepartures = await TryLocalCache(originStopId, destinationStopId, TransportTime.ToTransportZone(fromWhen));
            if (cachedDepartures.Count > 0)
            {
                _logger.LogInformation("Using {TripCount} cached trip entries", cachedDepartures.Count);
                return cachedDepartures;
            }

            var trips = await GetTrips(originStopId, destinationStopId, fromWhen, cancellationToken);
            return MapDepartures(trips);
        }

        private void ApplyBaseUrl(Action<string> apply)
        {
            // CR-36: honour TransportOpenData:BaseUrl; blank keeps the generated default
            var baseUrl = _config.Value.BaseUrl;
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                apply(baseUrl);
            }
        }

        // Replaced by DepartureMapper in Task 3
        private static List<TripSummary> MapDepartures(TripRequestResponse trips)
        {
            var output = new List<TripSummary>();

            foreach (var journey in trips.Journeys)
            {
                var origin = journey.Legs.First().Origin;
                var destination = journey.Legs.Last().Destination;

                output.Add(new TripSummary
                {
                    Origin = TimePlace.Factory(ParseApiTime(origin.DepartureTimeEstimated), origin.DisassembledName),
                    Destination = TimePlace.Factory(ParseApiTime(destination.ArrivalTimeEstimated), destination.DisassembledName)
                });
            }

            return output;
        }

        private static DateTimeOffset ParseApiTime(string value) =>
            TransportTime.ToTransportZone(DateTimeOffset.Parse(value, CultureInfo.InvariantCulture));

        // Replaced by TripFileCache in Task 4
        private async Task<List<TripSummary>> TryLocalCache(string originStopId, string destinationStopId, DateTimeOffset fromWhen)
        {
            // Transport NSW data connection times aren't great, so allow override via file cache
            var cacheFolder = Environment.GetEnvironmentVariable("AWTRIXSHARP_SETTINGS__DATA_DIRECTORY");

            if (!string.IsNullOrEmpty(cacheFolder))
            {
                var cacheFilename = $"trip_{originStopId}_{destinationStopId}_{fromWhen:HH}.json";
                var fullCachePath = Path.Combine(cacheFolder, cacheFilename);
                if (File.Exists(fullCachePath))
                {
                    _logger.LogInformation("Loading trip data from {CacheFile}", fullCachePath);
                    var cachedJson = await File.ReadAllTextAsync(fullCachePath);
                    var cachedTrips = JsonSerializer.Deserialize<List<TripSummary>>(cachedJson)!;

                    var now = DateTimeOffset.Now;
                    foreach (var trip in cachedTrips)
                    {
                        // Adjust times to be today
                        trip.Origin.Time = new DateTimeOffset(now.Year, now.Month, now.Day,
                            trip.Origin.Time.Hour, trip.Origin.Time.Minute, trip.Origin.Time.Second, now.Offset);
                        trip.Destination.Time = new DateTimeOffset(now.Year, now.Month, now.Day,
                            trip.Destination.Time.Hour, trip.Destination.Time.Minute, trip.Destination.Time.Second, now.Offset);
                    }

                    return cachedTrips;
                }
            }

            return new List<TripSummary>();
        }
    }
}
```

- [ ] **Step 6: Controller**

In `src/api/Controllers/TripPlannerController.cs`:

1. Add `using System.Threading;` after `using System.Text.Json;`.

2. Replace

```csharp
        public async Task<IActionResult> FindStops([FromQuery] string query)
        {
            try
            {
                var result = await _tripPlannerService.FindStops(query);
```

with

```csharp
        public async Task<IActionResult> FindStops([FromQuery] string query, CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await _tripPlannerService.FindStops(query, cancellationToken);
```

3. Replace

```csharp
        public async Task<IActionResult> GetDepartures([FromQuery] string originId, [FromQuery] string destinationId, [FromQuery] string fromDateTime)
        {
            try
            {
                var fromTimestamp = DateTime.Parse(fromDateTime);
                var result = await _tripPlannerService.GetNextDepartures(originId, destinationId, fromTimestamp);
```

with

```csharp
        public async Task<IActionResult> GetDepartures([FromQuery] string originId, [FromQuery] string destinationId, [FromQuery] string fromDateTime, CancellationToken cancellationToken = default)
        {
            try
            {
                // An offset-less value is Sydney wall clock, whatever the host TZ (CR-26)
                if (!TransportTime.TryParseQuery(fromDateTime, out var fromTimestamp))
                {
                    throw new FormatException($"'{fromDateTime}' is not a recognised date/time"); // 500 as before; WS7 makes this a 400
                }

                var result = await _tripPlannerService.GetNextDepartures(originId, destinationId, fromTimestamp, cancellationToken);
```

4. Replace

```csharp
        public async Task<IActionResult> GetTrip([FromQuery] string originId, [FromQuery] string destinationId, [FromQuery] string fromDateTime)
        {
            try
            {
                var fromTimestamp = DateTime.Parse(fromDateTime);
                var result = await _tripPlannerService.GetTrips(originId, destinationId, fromTimestamp);
```

with

```csharp
        public async Task<IActionResult> GetTrip([FromQuery] string originId, [FromQuery] string destinationId, [FromQuery] string fromDateTime, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!TransportTime.TryParseQuery(fromDateTime, out var fromTimestamp))
                {
                    throw new FormatException($"'{fromDateTime}' is not a recognised date/time"); // 500 as before; WS7 makes this a 400
                }

                var result = await _tripPlannerService.GetTrips(originId, destinationId, fromTimestamp, cancellationToken);
```

- [ ] **Step 7: Composition root, config default, README**

In `src/api/Program.cs`, replace

```csharp
            // Trip planner
            services.AddTransient<TripPlannerService>();
            services.AddTransient<ITripPlannerService>(sp => sp.GetRequiredService<TripPlannerService>());

            services.AddHttpClient<StopfinderClient>((serviceProvider, client) =>
            {
                var config = serviceProvider.GetRequiredService<IOptions<TransportOpenDataConfig>>();

                // Set the authorization header
                client.DefaultRequestHeaders.Add("Authorization", $"apikey {config.Value.ApiKey}");
            });

            services.AddHttpClient<TripClient>((serviceProvider, client) =>
            {
                var config = serviceProvider.GetRequiredService<IOptions<TransportOpenDataConfig>>();

                // Set the authorization header
                client.DefaultRequestHeaders.Add("Authorization", $"apikey {config.Value.ApiKey}");
            });
```

with

```csharp
            // Trip planner: a singleton that builds NSwag clients per call over this named client (CR-29)
            services.AddHttpClient(TripPlannerService.HttpClientName, (serviceProvider, client) =>
            {
                var config = serviceProvider.GetRequiredService<IOptions<TransportOpenDataConfig>>();

                client.Timeout = TripPlannerService.HttpTimeout;
                client.DefaultRequestHeaders.Add("Authorization", $"apikey {config.Value.ApiKey}");
            });
            services.AddSingleton<TripPlannerService>();
            services.AddSingleton<ITripPlannerService>(sp => sp.GetRequiredService<TripPlannerService>());
```

Leave the `services.Configure<TransportOpenDataConfig>(...)` block above it unchanged (WS7 replaces it).

In `src/transportOpenData/TransportOpenDataConfig.cs`, replace

```csharp
        public string BaseUrl { get; set; } = "https://api.transport.nsw.gov.au/v1";
```

with

```csharp
        public string BaseUrl { get; set; } = "https://api.transport.nsw.gov.au/v1/tp";
```

In `readme.md`, insert this section immediately above the line `## Development`:

```markdown
### Time zones

- **Trip planner queries always use Sydney time.** Transport NSW reads and returns Sydney wall-clock times, so AwtrixSharp converts to `Australia/Sydney` itself. Containers without `TZ` (UTC) get the right trains.
- **`CronSchedule` and `DiurnalApp` times use the host's local time zone.** Set `TZ` on the container, as in the compose example above. Otherwise a `CronSchedule` of `10 6 * * 1-5` fires at 06:10 UTC.
- `GET api/TripPlanner/departures` and `api/TripPlanner/trip` read a `fromDateTime` without an offset (e.g. `2025-09-01T06:00`) as Sydney time. Add an offset or `Z` to pass an exact instant.

```

- [ ] **Step 8: TripTimerApp call site**

In `src/api/Apps/TripTimer/TripTimerApp.cs` (post-WS4 `OnActivateAsync`), replace

```csharp
            var departures = await _tripPlanner
                .GetNextDepartures(Config.StopIdOrigin, Config.StopIdDestination, earliestDeparture.LocalDateTime)
                .WaitAsync(activation.Token);
```

with

```csharp
            // The service converts the instant to Sydney time (CR-26) and honours the token (CR-29)
            var departures = await _tripPlanner
                .GetNextDepartures(Config.StopIdOrigin, Config.StopIdDestination, earliestDeparture, activation.Token);
```

(Pre-WS4 adaptation, from Task 0 Step 1: in `ActivateScheduledWork`, change `earliestDeparture.LocalDateTime)` to `earliestDeparture, cts.Token)`.)

- [ ] **Step 9: Update the Moq setups of `GetNextDepartures`**

In each file recorded in Task 0 Step 4:
- `test/Test/Apps/TripTimer/TripTimerAppBoundaryTests.cs`
- `test/Test/Apps/TripTimer/TripTimerAppTickTests.cs`
- `test/Test/Apps/TripTimerAppTests.cs`
- `test/Test/HostedServices/ConductorStartupTests.cs`

replace every occurrence of

```csharp
GetNextDepartures(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>())
```

with

```csharp
GetNextDepartures(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())
```

Run: `grep -rn "IsAny<DateTime>()" test/Test --include=*.cs`
Expected: no matches in a `GetNextDepartures`/`GetTrips` setup.

- [ ] **Step 10: Run the tests and confirm they pass**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.TripPlanner|FullyQualifiedName~Test.CompositionRootTests|FullyQualifiedName~Test.Apps.TripTimer|FullyQualifiedName~Test.Apps.TripTimerAppTests|FullyQualifiedName~ConductorStartupTests"`
Expected: PASS, 0 failed, 1 skipped (`GetNextDepartures_NullEstimatedTime_FallsBackToPlannedTime`).

Run: `dotnet test test/transportOpenData.Tests/TransportOpenData.Tests.csproj --filter "FullyQualifiedName~TransportOpenDataConfigTests"`
Expected: PASS.

- [ ] **Step 11: Run the full suite and code checks**

Run: `dotnet test`
Expected: 0 failed.

Run each check. The expected result follows each arrow.
- `grep -n "AddHttpClient<TripClient>\|AddHttpClient<StopfinderClient>\|AddTransient<TripPlannerService>" src/api/Program.cs` → no matches.
- `grep -rn "LocalDateTime\|TimeZoneInfo.Local\|DateTime.Parse(" src/api/Services/TripPlanner src/api/Apps/TripTimer src/api/Controllers/TripPlannerController.cs` → no matches.
- `grep -rn "Mock<TripClient>\|Mock<StopfinderClient>" test/Test` → no matches.

- [ ] **Step 12: Commit**

```bash
git add src/api/Services/TripPlanner/TransportTime.cs src/api/Interfaces/ITripPlannerService.cs src/api/Services/TripPlanner/TripPlannerService.cs src/api/Controllers/TripPlannerController.cs src/api/Program.cs src/api/Apps/TripTimer/TripTimerApp.cs src/transportOpenData/TransportOpenDataConfig.cs readme.md test/Test/Test.csproj test/Test/TripPlanner/TripPlannerTestDoubles.cs test/Test/TripPlanner/TransportTimeTests.cs test/Test/TripPlanner/TripPlannerServiceTests.cs test/Test/TripPlanner/TripPlannerControllerTests.cs test/Test/CompositionRootTests.cs test/transportOpenData.Tests/Config/TransportOpenDataConfigTests.cs test/Test/Apps/TripTimer/TripTimerAppBoundaryTests.cs test/Test/Apps/TripTimer/TripTimerAppTickTests.cs test/Test/Apps/TripTimerAppTests.cs test/Test/HostedServices/ConductorStartupTests.cs
git commit -m "fix(trip-planner): singleton over IHttpClientFactory, Sydney query time, cancellation (CR-29, CR-26, CR-36)

TripPlannerService is a singleton that builds NSwag clients per call over a
named client with a 15 s timeout, applies TransportOpenData:BaseUrl, and
passes a CancellationToken to the request. GetTrips/GetNextDepartures take
an instant and query TfNSW in Sydney wall-clock time, so UTC containers get
the right trains. Offset-less controller dates are Sydney time. Service and
controller tests now run the real generated clients over a stub handler.
README documents TZ for cron and Diurnal.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

### Task 3: Tolerant departure mapping: error bodies, missing times, walks, duplicates, cancellations (CR-10, CR-27, CR-28)

**Files:**
- Create: `src/api/Services/TripPlanner/DepartureMapper.cs`
- Modify: `src/api/Services/TripPlanner/TripPlannerService.cs` (`GetNextDepartures`; delete `MapDepartures`/`ParseApiTime`)
- Modify: `test/Test/TripPlanner/TripPlannerServiceTests.cs` (remove one `Skip`)
- Test: `test/Test/TripPlanner/DepartureMappingTests.cs`

**Interfaces:**
- Consumes (Task 1): `TripRequestResponseJourneyLeg.RealtimeStatus`. Consumes (Task 2): `TransportTime.TryParseApiTime`, `TripPlannerTestData`, `StubHttpMessageHandler`.
- Produces: `internal static class DepartureMapper { static List<TripSummary> Map(TripRequestResponse? response, ILogger logger); static bool IsFootpath(TripRequestResponseJourneyLeg leg); static bool IsCancelled(TripRequestResponseJourneyLeg leg); }`

- [ ] **Step 1: Write the failing tests**

`test/Test/TripPlanner/DepartureMappingTests.cs`:

```csharp
using System.Globalization;
using System.Net;
using AwtrixSharpWeb.Services.TripPlanner;
using Microsoft.Extensions.Logging;
using Moq;
using TransportOpenData.TripPlanner;
using static Test.TripPlanner.TripPlannerTestData;

namespace Test.TripPlanner
{
    /// <summary>
    /// GetNextDepartures over real fixtures and hand-built responses: tolerant of error bodies and gaps (CR-10), boards the
    /// first transit leg rather than a leading walk and de-duplicates (CR-27), skips cancelled services (CR-28).
    /// </summary>
    public class DepartureMappingTests
    {
        private static readonly DateTimeOffset FromWhen = DateTimeOffset.Parse("2025-08-16T15:30:00+10:00", CultureInfo.InvariantCulture);
        private readonly Mock<ILogger<TripPlannerService>> _logger = new();

        private Task<List<TripSummary>> DeparturesFor(string json, HttpStatusCode status = HttpStatusCode.OK) =>
            CreateService(StubHttpMessageHandler.Json(json, status), _logger.Object).GetNextDepartures("222316", "200070", FromWhen);

        private Task<List<TripSummary>> DeparturesFor(TripRequestResponse response) => DeparturesFor(ToJson(response));

        private static DateTimeOffset At(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

        private static TripRequestResponseJourney GoodJourney(string departs = "2025-08-16T05:53:00Z", string place = "Oatley") =>
            Journey(Leg(Stop(departs, null, place), Stop("2025-08-16T06:40:00Z", null, "Central"), productClass: 5, "MONITORED"));

        private void VerifyWarning(string containing) =>
            _logger.Verify(x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains(containing)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);

        [Fact]
        public async Task ErrorBody_ReturnsNoDepartures_AndLogsTheApiMessage()
        {
            var result = await DeparturesFor(Fixture("ErrorResponse.json"));

            Assert.Empty(result);
            VerifyWarning("not been authenticated");
        }

        [Fact]
        public async Task HttpError_StillThrows_SoTheCallerKeepsItsLastGoodList()
        {
            await Assert.ThrowsAnyAsync<TripPlannerException>(() => DeparturesFor("{}", HttpStatusCode.ServiceUnavailable));
        }

        [Fact]
        public async Task ComplexTripResponse_BoardsTheFirstTransitLeg_SkippingLeadingWalks_AndDeduplicates()
        {
            var result = await DeparturesFor(Fixture("ComplexTripResponse.json"));

            // J2 and J6 start with a class-100 walk (05:41:30Z, 06:04Z); J4 and J8 repeat J3 and J7's departures
            Assert.Equal(
                new[] { "15:37:54", "16:00:30", "15:53:00", "16:03:00", "16:23:00", "16:13:00" },
                result.Select(d => d.Origin.Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture)));
            Assert.All(result, d => Assert.Equal(TimeSpan.FromHours(10), d.Origin.Time.Offset));
            Assert.DoesNotContain(result, d => d.Origin.Time == At("2025-08-16T05:41:30Z"));
            Assert.Equal("Macquarie Pl at The Strand", result[1].Origin.Place);
            Assert.Equal("Town Hall Station, Platform 3", result[0].Destination.Place);
        }

        [Fact]
        public async Task SuccessfulTripResponse_FallsBackToStopSequenceTimes()
        {
            // The only leg's origin and destination carry no times; stopSequence does
            var summary = Assert.Single(await DeparturesFor(Fixture("SuccessfulTripResponse.json")));

            Assert.Equal(At("2023-06-01T12:01:00Z"), summary.Origin.Time);
            Assert.Equal("Central", summary.Origin.Place);
            Assert.Equal(At("2023-06-01T12:11:00Z"), summary.Destination.Time);
            Assert.Equal("Circular Quay", summary.Destination.Place);
        }

        [Fact]
        public async Task MissingOrUnparsableEstimate_FallsBackToPlanned()
        {
            var summary = Assert.Single(await DeparturesFor(Response(Journey(Leg(
                Stop("soon", "2025-08-16T05:53:00Z", "Oatley"),
                Stop(null, "2025-08-16T06:40:00Z", "Central"))))));

            Assert.Equal(At("2025-08-16T05:53:00Z"), summary.Origin.Time);
            Assert.Equal(At("2025-08-16T06:40:00Z"), summary.Destination.Time);
        }

        [Fact]
        public async Task JourneyWithoutAnyDepartureTime_IsSkipped_AndTheOthersAreKept()
        {
            var result = await DeparturesFor(Response(
                Journey(Leg(Stop(null, null, "NoTimes"), Stop(null, null, "Nowhere"))),
                GoodJourney()));

            Assert.Equal("Oatley", Assert.Single(result).Origin.Place);
            VerifyWarning("no departure time");
        }

        [Fact]
        public async Task MissingArrival_UsesTheDepartureTime()
        {
            var summary = Assert.Single(await DeparturesFor(Response(Journey(Leg(
                Stop("2025-08-16T05:53:00Z", null, "Oatley"),
                Stop(null, null, "Central"))))));

            Assert.Equal(summary.Origin.Time, summary.Destination.Time);
        }

        [Fact]
        public async Task NullJourneys_NullLegsAndEmptyLegs_AreSkipped()
        {
            var result = await DeparturesFor(Response(
                null,
                new TripRequestResponseJourney { Legs = null },
                new TripRequestResponseJourney { Legs = new List<TripRequestResponseJourneyLeg>() },
                GoodJourney()));

            Assert.Equal("Oatley", Assert.Single(result).Origin.Place);
        }

        [Fact]
        public async Task WalkOnlyJourney_IsSkipped()
        {
            var result = await DeparturesFor(Response(
                Journey(Leg(Stop("2025-08-16T05:40:00Z", null, "Home"), Stop("2025-08-16T06:10:00Z", null, "Work"), productClass: 99)),
                GoodJourney()));

            Assert.Equal("Oatley", Assert.Single(result).Origin.Place);
        }

        [Theory]
        [InlineData("CANCELLED")]
        [InlineData("TRIP_CANCELLED")]
        [InlineData("cancelled")]
        public async Task JourneyWithACancelledService_IsSkipped(string status)
        {
            var result = await DeparturesFor(Response(
                Journey(
                    Leg(Stop("2025-08-16T05:30:00Z", null, "Home"), Stop("2025-08-16T05:40:00Z", null, "Oatley"), productClass: 100),
                    Leg(Stop("2025-08-16T05:45:00Z", null, "Oatley"), Stop("2025-08-16T06:30:00Z", null, "Central"), productClass: 1, status)),
                GoodJourney()));

            Assert.Equal(At("2025-08-16T05:53:00Z"), Assert.Single(result).Origin.Time);
        }

        [Fact]
        public async Task DuplicateDepartureInstants_KeepTheFirstJourney()
        {
            var result = await DeparturesFor(Response(
                GoodJourney(place: "First"),
                GoodJourney(place: "Second")));

            Assert.Equal("First", Assert.Single(result).Origin.Place);
        }
    }
}
```

In `test/Test/TripPlanner/TripPlannerServiceTests.cs`, replace

```csharp
        [Fact(Skip = "Known bug (fixed in WS6 Task 3): no fallback from a null estimated time to the planned time.")]
```

with

```csharp
        [Fact]
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~DepartureMappingTests|FullyQualifiedName~TripPlannerServiceTests"`
Expected: FAIL. Examples:
- `ErrorBody_ReturnsNoDepartures_AndLogsTheApiMessage` (`NullReferenceException` on `Journeys`)
- `ComplexTripResponse_...` (8 items including 15:41:30)
- `GetNextDepartures_NullEstimatedTime_FallsBackToPlannedTime` (`ArgumentNullException`)

`HttpError_StillThrows_...` passes already.

- [ ] **Step 3: Implement**

`src/api/Services/TripPlanner/DepartureMapper.cs`:

```csharp
using TransportOpenData.TripPlanner;

namespace AwtrixSharpWeb.Services.TripPlanner
{
    /// <summary>
    /// Turns a Trip Planner response into departures from the first boarded service. Never throws: an error body,
    /// a journey with no legs or times, or one malformed journey is logged and skipped without losing the others (CR-10).
    /// </summary>
    internal static class DepartureMapper
    {
        /// <summary>TfNSW product classes for walking legs: 99 footpath, 100 walk between stops (CR-27)</summary>
        private static readonly int[] FootpathProductClasses = { 99, 100 };

        public static List<TripSummary> Map(TripRequestResponse? response, ILogger logger)
        {
            var output = new List<TripSummary>();

            if (response == null)
            {
                logger.LogWarning("Trip planner returned an empty body");
                return output;
            }

            if (response.Error != null)
            {
                logger.LogWarning("Trip planner returned an error: {Message}", response.Error.Message);
            }

            if (response.Journeys == null)
            {
                return output;
            }

            var seenDepartures = new HashSet<DateTimeOffset>();
            var index = 0;

            foreach (var journey in response.Journeys)
            {
                index++;
                try
                {
                    var summary = MapJourney(journey, index, logger);
                    if (summary == null)
                    {
                        continue;
                    }

                    if (!seenDepartures.Add(summary.Origin.Time))
                    {
                        logger.LogDebug("Journey {Index} duplicates the {Departure:HH:mm:ss} departure; skipped", index, summary.Origin.Time);
                        continue;
                    }

                    output.Add(summary);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Journey {Index} could not be read; skipped", index);
                }
            }

            return output;
        }

        internal static bool IsFootpath(TripRequestResponseJourneyLeg leg) =>
            leg.Transportation?.Product?.Class is int productClass && FootpathProductClasses.Contains(productClass);

        /// <summary>
        /// CR-28: TfNSW's exact cancellation value is unconfirmed (repo fixtures only show MONITORED), so any realtime
        /// status containing "CANCEL" counts (CANCELLED, TRIP_CANCELLED, ...).
        /// </summary>
        internal static bool IsCancelled(TripRequestResponseJourneyLeg leg) =>
            leg.RealtimeStatus?.Any(status => status != null && status.Contains("CANCEL", StringComparison.OrdinalIgnoreCase)) == true;

        private static TripSummary? MapJourney(TripRequestResponseJourney? journey, int index, ILogger logger)
        {
            var legs = journey?.Legs?.Where(leg => leg != null).ToList() ?? new List<TripRequestResponseJourneyLeg>();
            if (legs.Count == 0)
            {
                logger.LogWarning("Journey {Index} has no legs; skipped", index);
                return null;
            }

            var transitLegs = legs.Where(leg => !IsFootpath(leg)).ToList();
            if (transitLegs.Count == 0)
            {
                logger.LogDebug("Journey {Index} is walking only; skipped", index);
                return null;
            }

            if (transitLegs.Any(IsCancelled))
            {
                logger.LogInformation("Journey {Index} includes a cancelled service; skipped", index);
                return null;
            }

            var boarding = transitLegs[0];
            var final = legs[^1];

            var firstStop = boarding.StopSequence?.FirstOrDefault();
            if (!FirstTime(out var departs,
                    boarding.Origin?.DepartureTimeEstimated,
                    boarding.Origin?.DepartureTimePlanned,
                    firstStop?.DepartureTimeEstimated,
                    firstStop?.DepartureTimePlanned))
            {
                logger.LogWarning("Journey {Index} has no departure time; skipped", index);
                return null;
            }

            var lastStop = final.StopSequence?.LastOrDefault();
            if (!FirstTime(out var arrives,
                    final.Destination?.ArrivalTimeEstimated,
                    final.Destination?.ArrivalTimePlanned,
                    lastStop?.ArrivalTimeEstimated,
                    lastStop?.ArrivalTimePlanned))
            {
                arrives = departs; // only the departure drives the alarm
            }

            return new TripSummary
            {
                Origin = TimePlace.Factory(departs, PlaceName(boarding.Origin)),
                Destination = TimePlace.Factory(arrives, PlaceName(final.Destination))
            };
        }

        /// <summary>Estimated before planned: TfNSW's estimated time is the realtime value</summary>
        private static bool FirstTime(out DateTimeOffset time, params string?[] candidates)
        {
            foreach (var candidate in candidates)
            {
                if (TransportTime.TryParseApiTime(candidate, out time))
                {
                    return true;
                }
            }

            time = default;
            return false;
        }

        private static string PlaceName(TripRequestResponseJourneyLegStop? stop) => stop?.DisassembledName ?? stop?.Name ?? string.Empty;
    }
}
```

In `src/api/Services/TripPlanner/TripPlannerService.cs`:

1. In `GetNextDepartures`, replace `return MapDepartures(trips);` with `return DepartureMapper.Map(trips, _logger);`.
2. Delete the `// Replaced by DepartureMapper in Task 3` comment, the `MapDepartures` method and the `ParseApiTime` method.
3. Delete `using System.Globalization;`.

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~DepartureMappingTests|FullyQualifiedName~TripPlannerServiceTests|FullyQualifiedName~TripPlannerControllerTests"`
Expected: PASS, 0 failed, 0 skipped.

- [ ] **Step 5: Run the full suite and code checks**

Run: `dotnet test`
Expected: 0 failed. Only the `TripSummary.Factory` test (WS8) is skipped.

Run each check. The expected result follows each arrow.
- `grep -n "Skip =" test/Test/TripPlanner/TripPlannerServiceTests.cs` → no matches.
- `grep -n "Legs.First()\|DateTimeOffset.Parse" src/api/Services/TripPlanner/TripPlannerService.cs src/api/Services/TripPlanner/DepartureMapper.cs` → no matches.

- [ ] **Step 6: Commit**

```bash
git add src/api/Services/TripPlanner/DepartureMapper.cs src/api/Services/TripPlanner/TripPlannerService.cs test/Test/TripPlanner/DepartureMappingTests.cs test/Test/TripPlanner/TripPlannerServiceTests.cs
git commit -m "fix(trip-planner): tolerate bad journeys; board the first transit leg; skip cancelled (CR-10, CR-27, CR-28)

An error body, null legs or a journey with no time are logged and skipped
instead of throwing away every departure. Times fall back from estimated to
planned to the stop sequence. A leading walk (product class 99/100) is no
longer taken as the train, duplicate departures collapse to one, and journeys
whose realtimeStatus mentions CANCEL are dropped. Un-skips the null
estimated time test.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

### Task 4: Harden the trip file cache (CR-37)

**Files:**
- Create: `src/api/Services/TripPlanner/TripFileCache.cs`
- Modify: `src/api/Services/TripPlanner/TripPlannerService.cs` (`GetNextDepartures`; delete `TryLocalCache`)
- Modify: `test/Test/TripPlanner/TripPlannerServiceTests.cs` (replace the cache test, add a fallback test)
- Test: `test/Test/TripPlanner/TripFileCacheTests.cs`

**Interfaces:**
- Consumes (Task 2): `TransportTime.ToTransportZone`, `TransportTime.FromTransportWallClock`.
- Produces: `internal sealed class TripFileCache`:
  - `TripFileCache(string? directory, ILogger logger)`
  - `public const string DataDirectoryVariable = "AWTRIXSHARP_SETTINGS__DATA_DIRECTORY"`
  - `internal static readonly TimeSpan RolloverTolerance`
  - `Task<List<TripSummary>?> TryLoadAsync(string originStopId, string destinationStopId, DateTimeOffset fromWhen, CancellationToken cancellationToken = default)`
  - `internal static TripSummary Redate(TripSummary trip, DateTimeOffset queryTime)`

- [ ] **Step 1: Write the failing tests**

`test/Test/TripPlanner/TripFileCacheTests.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using AwtrixSharpWeb.Services.TripPlanner;
using Microsoft.Extensions.Logging;
using Moq;

namespace Test.TripPlanner
{
    /// <summary>
    /// CR-37: the opt-in cache never breaks a lookup (null means "use the API"), keys on the Sydney hour, rejects unsafe
    /// stop ids, and re-dates entries onto the query date with a post-midnight rollover. The directory is passed in,
    /// so these tests never touch the environment variable.
    /// </summary>
    public sealed class TripFileCacheTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "awtrixsharp-cache-tests-" + Guid.NewGuid());
        private readonly Mock<ILogger> _logger = new();

        public TripFileCacheTests()
        {
            Directory.CreateDirectory(_directory);
        }

        public void Dispose()
        {
            Directory.Delete(_directory, recursive: true);
        }

        private TripFileCache Cache() => new(_directory, _logger.Object);

        private static DateTimeOffset At(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

        private void WriteTrips(string fileName, params (string Departs, string Arrives, string Place)[] trips)
        {
            var summaries = trips.Select(t => new TripSummary
            {
                Origin = TimePlace.Factory(At(t.Departs), t.Place),
                Destination = TimePlace.Factory(At(t.Arrives), "Destination")
            }).ToList();
            File.WriteAllText(Path.Combine(_directory, fileName), JsonSerializer.Serialize(summaries));
        }

        private void VerifyWarned() =>
            _logger.Verify(x => x.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public async Task NoDirectory_ReturnsNull(string? directory)
        {
            Assert.Null(await new TripFileCache(directory, _logger.Object).TryLoadAsync("1", "2", At("2025-01-01T08:00:00+11:00")));
        }

        [Fact]
        public async Task MissingFile_ReturnsNull()
        {
            Assert.Null(await Cache().TryLoadAsync("1", "2", At("2025-01-01T08:00:00+11:00")));
        }

        [Fact]
        public async Task SameDayEntry_IsRedatedOntoTheQueryDate_KeepingWallClockSecondsAndPlace()
        {
            WriteTrips("trip_200080_200060_08.json", ("2000-01-01T08:30:15+11:00", "2000-01-01T09:00:45+11:00", "Central"));

            var trips = await Cache().TryLoadAsync("200080", "200060", At("2025-01-01T08:00:00+11:00"));

            var trip = Assert.Single(trips!);
            Assert.Equal(At("2025-01-01T08:30:15+11:00"), trip.Origin.Time);
            Assert.Equal(TimeSpan.FromHours(11), trip.Origin.Time.Offset);
            Assert.Equal("Central", trip.Origin.Place);
            Assert.Equal(At("2025-01-01T09:00:45+11:00"), trip.Destination.Time);
        }

        [Fact]
        public async Task HourKey_IsTheSydneyHour_WhateverTheCallersOffset()
        {
            WriteTrips("trip_1_2_08.json", ("2000-01-01T08:45:00+11:00", "2000-01-01T09:15:00+11:00", "Central"));

            var trips = await Cache().TryLoadAsync("1", "2", At("2024-12-31T21:30:00Z")); // 08:30 AEDT on 1 January

            Assert.Equal(At("2025-01-01T08:45:00+11:00"), Assert.Single(trips!).Origin.Time);
        }

        [Fact]
        public async Task EntriesAfterMidnight_RollForwardADay()
        {
            WriteTrips("trip_1_2_23.json",
                ("2000-01-01T23:00:00+11:00", "2000-01-01T23:20:00+11:00", "WithinTolerance"),
                ("2000-01-01T23:45:00+11:00", "2000-01-01T23:59:00+11:00", "SameDay"),
                ("2000-01-01T23:50:00+11:00", "2000-01-02T00:20:00+11:00", "ArrivesTomorrow"),
                ("2000-01-02T00:10:00+11:00", "2000-01-02T00:40:00+11:00", "Tomorrow"));

            var trips = (await Cache().TryLoadAsync("1", "2", At("2025-01-01T23:30:00+11:00")))!;

            Assert.Equal(At("2025-01-01T23:00:00+11:00"), trips.Single(t => t.Origin.Place == "WithinTolerance").Origin.Time);
            Assert.Equal(At("2025-01-01T23:45:00+11:00"), trips.Single(t => t.Origin.Place == "SameDay").Origin.Time);
            Assert.Equal(At("2025-01-02T00:20:00+11:00"), trips.Single(t => t.Origin.Place == "ArrivesTomorrow").Destination.Time);
            Assert.Equal(At("2025-01-02T00:10:00+11:00"), trips.Single(t => t.Origin.Place == "Tomorrow").Origin.Time);
        }

        [Fact]
        public async Task EntryOnADstStartDate_GetsThatDatesOffset()
        {
            WriteTrips("trip_1_2_01.json", ("2000-01-01T03:10:00+10:00", "2000-01-01T03:40:00+10:00", "AfterSpringForward"));

            var trips = await Cache().TryLoadAsync("1", "2", At("2024-10-06T01:30:00+10:00"));

            var trip = Assert.Single(trips!);
            Assert.Equal(At("2024-10-06T03:10:00+11:00"), trip.Origin.Time);
            Assert.Equal(TimeSpan.FromHours(11), trip.Origin.Time.Offset);
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("null")]
        [InlineData("[]")]
        [InlineData("[null]")]
        public async Task UnusableFile_ReturnsNull_AndWarns(string content)
        {
            File.WriteAllText(Path.Combine(_directory, "trip_1_2_08.json"), content);

            Assert.Null(await Cache().TryLoadAsync("1", "2", At("2025-01-01T08:00:00+11:00")));
            VerifyWarned();
        }

        [Theory]
        [InlineData("../secret", "2")]
        [InlineData("1", "a/b")]
        [InlineData("1", "a\\b")]
        [InlineData("", "2")]
        public async Task UnsafeStopIds_ReturnNull(string origin, string destination)
        {
            Assert.Null(await Cache().TryLoadAsync(origin, destination, At("2025-01-01T08:00:00+11:00")));
        }
    }
}
```

In `test/Test/TripPlanner/TripPlannerServiceTests.cs`, replace the whole `GetNextDepartures_UsesFileCache_WhenPresent_AndDoesNotCallTheApi` test with these two tests:

```csharp
        [Fact]
        public async Task GetNextDepartures_UsesFileCache_WhenPresent_AndDoesNotCallTheApi()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "awtrixsharp-tests-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            var previousValue = Environment.GetEnvironmentVariable(DataDirectoryVariable);

            try
            {
                var fromWhen = new DateTimeOffset(2025, 1, 1, 8, 0, 0, TimeSpan.FromHours(11)); // 08:00 Sydney (AEDT)
                var cachedTrips = new List<TripSummary>
                {
                    new()
                    {
                        Origin = new TimePlace { Time = new DateTimeOffset(2000, 1, 1, 8, 30, 15, TimeSpan.FromHours(11)), Place = "CachedOrigin" },
                        Destination = new TimePlace { Time = new DateTimeOffset(2000, 1, 1, 9, 0, 45, TimeSpan.FromHours(11)), Place = "CachedDestination" }
                    }
                };
                File.WriteAllText(Path.Combine(tempDir, "trip_originA_destB_08.json"), JsonSerializer.Serialize(cachedTrips));
                Environment.SetEnvironmentVariable(DataDirectoryVariable, tempDir);

                var handler = StubHttpMessageHandler.Json("{\"journeys\":[]}");
                var sut = CreateService(handler);

                var result = await sut.GetNextDepartures("originA", "destB", fromWhen);

                var summary = Assert.Single(result);
                Assert.Equal("CachedOrigin", summary.Origin.Place);
                Assert.Equal(new DateTimeOffset(2025, 1, 1, 8, 30, 15, TimeSpan.FromHours(11)), summary.Origin.Time); // the query's date, not today's
                Assert.Empty(handler.RequestUris);
            }
            finally
            {
                Environment.SetEnvironmentVariable(DataDirectoryVariable, previousValue);
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task GetNextDepartures_UnreadableCacheFile_FallsBackToTheApi()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "awtrixsharp-tests-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            var previousValue = Environment.GetEnvironmentVariable(DataDirectoryVariable);

            try
            {
                File.WriteAllText(Path.Combine(tempDir, "trip_200080_200060_09.json"), "{ not json");
                Environment.SetEnvironmentVariable(DataDirectoryVariable, tempDir);
                var (sut, handler) = ServiceReturning(Response(Journey(Leg(
                    Stop("2024-06-01T00:05:00Z", null, "Central"),
                    Stop("2024-06-01T00:35:00Z", null, "Circular Quay")))));

                var result = await sut.GetNextDepartures("200080", "200060", AnyTime); // 09:00 Sydney

                Assert.Equal("Central", Assert.Single(result).Origin.Place);
                Assert.Single(handler.RequestUris);
            }
            finally
            {
                Environment.SetEnvironmentVariable(DataDirectoryVariable, previousValue);
                Directory.Delete(tempDir, true);
            }
        }
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~TripFileCacheTests|FullyQualifiedName~TripPlannerServiceTests"`
Expected: build FAILS with `CS0246: The type or namespace name 'TripFileCache' could not be found`.

- [ ] **Step 3: Implement**

`src/api/Services/TripPlanner/TripFileCache.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AwtrixSharpWeb.Services.TripPlanner
{
    /// <summary>
    /// Optional departures cache for slow Transport NSW connections: {directory}/trip_{origin}_{destination}_{HH}.json holds a
    /// serialised List&lt;TripSummary&gt;, where HH is the Sydney hour of the query. Only wall-clock times are used; they are
    /// re-dated onto the query's Sydney date. Any problem returns null so the caller uses the live API (CR-37).
    /// </summary>
    internal sealed class TripFileCache
    {
        public const string DataDirectoryVariable = "AWTRIXSHARP_SETTINGS__DATA_DIRECTORY";

        /// <summary>A cached departure more than this far before the query time is taken to be after midnight</summary>
        internal static readonly TimeSpan RolloverTolerance = TimeSpan.FromHours(1);

        private static readonly Regex SafeStopId = new("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant);

        private readonly string? _directory;
        private readonly ILogger _logger;

        public TripFileCache(string? directory, ILogger logger)
        {
            _directory = directory;
            _logger = logger;
        }

        public async Task<List<TripSummary>?> TryLoadAsync(string originStopId, string destinationStopId, DateTimeOffset fromWhen, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_directory))
            {
                return null;
            }

            if (!SafeStopId.IsMatch(originStopId ?? string.Empty) || !SafeStopId.IsMatch(destinationStopId ?? string.Empty))
            {
                _logger.LogWarning("Trip cache not used: stop ids {Origin}/{Destination} may only contain letters, digits, '-' and '_'",
                    originStopId, destinationStopId);
                return null;
            }

            var queryTime = TransportTime.ToTransportZone(fromWhen);
            var fileName = string.Create(CultureInfo.InvariantCulture, $"trip_{originStopId}_{destinationStopId}_{queryTime:HH}.json");
            var path = Path.Combine(_directory, fileName);

            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                _logger.LogInformation("Loading trip data from {CacheFile}", path);

                List<TripSummary?>? cached;
                await using (var stream = File.OpenRead(path))
                {
                    cached = await JsonSerializer.DeserializeAsync<List<TripSummary?>>(stream, cancellationToken: cancellationToken);
                }

                var trips = cached?
                    .Where(trip => trip?.Origin != null && trip.Destination != null)
                    .Select(trip => Redate(trip!, queryTime))
                    .ToList();

                if (trips is not { Count: > 0 })
                {
                    _logger.LogWarning("Trip cache file {CacheFile} holds no trips; using the API", path);
                    return null;
                }

                return trips;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                _logger.LogWarning(ex, "Trip cache file {CacheFile} could not be read; using the API", path);
                return null;
            }
        }

        /// <summary>
        /// Put a cached trip's Sydney wall-clock times onto <paramref name="queryTime"/>'s date, rolling to the next day
        /// when the departure would be more than <see cref="RolloverTolerance"/> before the query.
        /// </summary>
        internal static TripSummary Redate(TripSummary trip, DateTimeOffset queryTime)
        {
            var queryDate = queryTime.DateTime.Date;
            var departTimeOfDay = trip.Origin.Time.TimeOfDay;

            var departs = TransportTime.FromTransportWallClock(queryDate + departTimeOfDay);
            if (departs < queryTime - RolloverTolerance)
            {
                departs = TransportTime.FromTransportWallClock(queryDate.AddDays(1) + departTimeOfDay);
            }

            var travel = trip.Destination.Time.TimeOfDay - departTimeOfDay;
            if (travel < TimeSpan.Zero)
            {
                travel += TimeSpan.FromDays(1);
            }

            return new TripSummary
            {
                Origin = TimePlace.Factory(departs, trip.Origin.Place),
                Destination = TimePlace.Factory(TransportTime.ToTransportZone(departs + travel), trip.Destination.Place)
            };
        }
    }
}
```

In `src/api/Services/TripPlanner/TripPlannerService.cs`:

1. Replace the body of `GetNextDepartures` with:

```csharp
        {
            // Opt-in file cache; any problem with it falls back to the API (CR-37)
            var cache = new TripFileCache(Environment.GetEnvironmentVariable(TripFileCache.DataDirectoryVariable), _logger);
            var cachedDepartures = await cache.TryLoadAsync(originStopId, destinationStopId, fromWhen, cancellationToken);
            if (cachedDepartures != null)
            {
                _logger.LogInformation("Using {TripCount} cached trip entries", cachedDepartures.Count);
                return cachedDepartures;
            }

            var trips = await GetTrips(originStopId, destinationStopId, fromWhen, cancellationToken);
            return DepartureMapper.Map(trips, _logger);
        }
```

2. Delete the `// Replaced by TripFileCache in Task 4` comment and the whole `TryLocalCache` method.
3. Delete `using System.Text.Json;`.

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~TripFileCacheTests|FullyQualifiedName~TripPlannerServiceTests|FullyQualifiedName~DepartureMappingTests"`
Expected: PASS, 0 failed.

- [ ] **Step 5: Run the full suite and code checks**

Run: `dotnet test`
Expected: 0 failed.

Run each check. The expected result follows each arrow.
- `grep -rn "DateTimeOffset.Now\|TryLocalCache" src/api/Services/TripPlanner` → no matches.
- `grep -rn "AWTRIXSHARP_SETTINGS__DATA_DIRECTORY" src/api/Services` → only `TripFileCache.cs` (the constant).

- [ ] **Step 6: Commit**

```bash
git add src/api/Services/TripPlanner/TripFileCache.cs src/api/Services/TripPlanner/TripPlannerService.cs test/Test/TripPlanner/TripFileCacheTests.cs test/Test/TripPlanner/TripPlannerServiceTests.cs
git commit -m "fix(trip-planner): trip file cache can no longer break a lookup (CR-37)

A missing, unreadable, empty or null cache file, or a stop id that is not a
plain token, now falls back to the live API instead of throwing. The cache
hour key is the Sydney hour, and cached wall-clock times are re-dated onto
the query's Sydney date with a next-day rollover for entries after midnight.
AWTRIXSHARP_SETTINGS__DATA_DIRECTORY keeps its meaning.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

### Task 5: TripTimer refreshes departures during the window (CR-25)

**Files:**
- Modify: `src/api/Apps/TripTimer/TripTimerApp.cs` (fields, constructor, `OnActivateAsync`, `ClockTickSecond`; new members)
- Test: `test/Test/Apps/TripTimer/TripTimerAppRefreshTests.cs`

**Interfaces:**
- Consumes (WS4):
  - `ScheduledActivation.Token`/`IsEnded`/`Complete()`/`Number`/`Trigger`
  - `CurrentActivation`, `LastRun`
  - `SetDepartures`, `NextDepartures`, `_nextDepartures`, `BuildMessage(DateTime)`
  - `FireAndLog`, `IClock.TimeProvider`
- Consumes (Task 2): `ITripPlannerService.GetNextDepartures(string, string, DateTimeOffset, CancellationToken)`.
- Produces:
  - `internal static readonly TimeSpan TripTimerApp.RefreshInterval` (2 min), `RetryInterval` (30 s)
  - `internal static TimeSpan NextRefreshDelay(int consecutiveFailures)`
  - `internal Func<TimeSpan, CancellationToken, Task> RefreshDelay { get; set; }`
  - `internal Task<bool> RefreshDeparturesAsync(ScheduledActivation activation)`

- [ ] **Step 1: Write the failing tests**

`test/Test/Apps/TripTimer/TripTimerAppRefreshTests.cs`:

```csharp
using System.Collections.Concurrent;
using System.Threading.Channels;
using AwtrixSharpWeb.Apps.TripTimer;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using AwtrixSharpWeb.Services.TripPlanner;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Test.Domain;

namespace Test.Apps.TripTimer
{
    /// <summary>
    /// CR-25: an active trip timer re-queries departures, keeps the last good list when a query fails, backs off,
    /// re-queries before giving up, and stops (cancelling its in-flight request) when the window ends.
    /// The refresh wait is scripted, so each loop step is released explicitly rather than by timers.
    /// </summary>
    public class TripTimerAppRefreshTests
    {
        private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

        // FakeTimeProvider's local zone is UTC, so every instant here is UTC
        private static readonly DateTimeOffset Now = new(2025, 8, 19, 6, 30, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset Departure = new(2025, 8, 19, 6, 41, 0, TimeSpan.Zero);

        private readonly FakeTimeProvider _time = new(Now);
        private readonly Mock<ILogger> _logger = new();
        private readonly Mock<IAwtrixService> _awtrix = new();
        private readonly Mock<ITimerService> _timer = new();
        private readonly Mock<ITripPlannerService> _planner = new();
        private readonly ScriptedDelay _delays = new();
        private readonly ConcurrentQueue<CancellationToken> _plannerTokens = new();
        private Func<CancellationToken, Task<List<TripSummary>>> _plannerResult = _ => Task.FromResult(Departures(Departure));

        public TripTimerAppRefreshTests()
        {
            _awtrix.Setup(a => a.Notify(It.IsAny<AwtrixAddress>(), It.IsAny<AwtrixAppMessage>())).ReturnsAsync(true);
            _awtrix.Setup(a => a.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>())).ReturnsAsync(true);
            _awtrix.Setup(a => a.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>())).ReturnsAsync(true);
            _planner
                .Setup(p => p.GetNextDepartures(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .Returns<string, string, DateTimeOffset, CancellationToken>((_, _, _, token) =>
                {
                    _plannerTokens.Enqueue(token);
                    return _plannerResult(token);
                });
        }

        private static List<TripSummary> Departures(params DateTimeOffset[] departs) =>
            departs.Select(depart => TripSummaryTests.Create(depart)).ToList();

        private TripTimerApp CreateApp()
        {
            var config = new TripTimerAppConfig
            {
                CronSchedule = "10 6 * * 1-5",
                ActiveTime = TimeSpan.FromMinutes(30),
                TimeToOrigin = TimeSpan.Zero,
                TimeToPrepare = TimeSpan.Zero,
            };
            config.Type = AppNames.TripTimerApp;

            var app = new TripTimerApp(_logger.Object, new Clock(_time), new AwtrixAddress { BaseTopic = "awtrix/clock1" },
                _awtrix.Object, _timer.Object, config, _planner.Object);
            app.RefreshDelay = _delays.Delay;
            return app;
        }

        private void RaiseSecond() =>
            _timer.Raise(t => t.SecondChanged += null, this, new ClockTickEventArgs(_time.GetLocalNow().DateTime));

        private int PlannerCalls => _planner.Invocations.Count;

        private static DateTimeOffset OnlyDeparture(TripTimerApp app) => Assert.Single(app.NextDepartures).Origin.Time;

        private void VerifyAppUpdates(Times times) =>
            _awtrix.Verify(a => a.AppUpdate(It.IsAny<AwtrixAddress>(), AppNames.TripTimerApp, It.IsAny<AwtrixAppMessage>()), times);

        private void VerifyNoErrorsLogged() =>
            _logger.Verify(x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);

        [Theory]
        [InlineData(0, 120)]
        [InlineData(1, 30)]
        [InlineData(2, 60)]
        [InlineData(3, 120)]
        [InlineData(40, 120)]
        public void NextRefreshDelay_BacksOffAfterFailures_UpToTheRefreshInterval(int consecutiveFailures, int expectedSeconds)
        {
            Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), TripTimerApp.NextRefreshDelay(consecutiveFailures));
        }

        [Fact]
        public async Task Refresh_AfterTheInterval_ReplacesTheDepartures()
        {
            var app = CreateApp();
            app.ExecuteNow();

            var wait = await _delays.NextAsync();
            Assert.Equal(TripTimerApp.RefreshInterval, wait.Delay);
            Assert.Equal(Departure, OnlyDeparture(app));

            _plannerResult = _ => Task.FromResult(Departures(Departure.AddMinutes(8))); // the 06:41 is now 8 minutes late
            wait.Release();

            Assert.Equal(TripTimerApp.RefreshInterval, (await _delays.NextAsync()).Delay);
            Assert.Equal(Departure.AddMinutes(8), OnlyDeparture(app));
            Assert.Equal(2, PlannerCalls);
            await app.DisposeAsync();
        }

        [Fact]
        public async Task FailedRefresh_KeepsTheLastGoodList_AndBacksOff()
        {
            var app = CreateApp();
            app.ExecuteNow();
            var wait = await _delays.NextAsync();

            _plannerResult = _ => Task.FromException<List<TripSummary>>(new HttpRequestException("503"));
            wait.Release();
            wait = await _delays.NextAsync();
            Assert.Equal(TimeSpan.FromSeconds(30), wait.Delay);
            Assert.Equal(Departure, OnlyDeparture(app));

            wait.Release();
            wait = await _delays.NextAsync();
            Assert.Equal(TimeSpan.FromSeconds(60), wait.Delay);
            Assert.Equal(Departure, OnlyDeparture(app));

            _plannerResult = _ => Task.FromResult(Departures(Departure.AddMinutes(4)));
            wait.Release();
            Assert.Equal(TripTimerApp.RefreshInterval, (await _delays.NextAsync()).Delay);
            Assert.Equal(Departure.AddMinutes(4), OnlyDeparture(app));

            VerifyNoErrorsLogged();
            await app.DisposeAsync();
        }

        [Fact]
        public async Task InitialQueryFailure_KeepsTheWindowOpen_UntilARetrySucceeds()
        {
            // CR-25: a transient failure at activation used to lose the whole window
            _plannerResult = _ => Task.FromException<List<TripSummary>>(new TaskCanceledException("HttpClient timeout"));
            var app = CreateApp();
            app.ExecuteNow();

            var wait = await _delays.NextAsync();
            Assert.Equal(TripTimerApp.RetryInterval, wait.Delay);

            RaiseSecond();
            Assert.False(app.LastRun.IsCompleted); // nothing loaded yet: the empty list does not end the window
            VerifyAppUpdates(Times.Never());

            _plannerResult = _ => Task.FromResult(Departures(Departure));
            wait.Release();
            Assert.Equal(TripTimerApp.RefreshInterval, (await _delays.NextAsync()).Delay);

            RaiseSecond();
            VerifyAppUpdates(Times.Once());
            VerifyNoErrorsLogged();
            await app.DisposeAsync();
        }

        [Fact]
        public async Task ExhaustedList_IsReQueriedOnce_BeforeTheWindowEnds()
        {
            var app = CreateApp();
            app.ExecuteNow();
            await _delays.NextAsync();
            _time.Advance(Departure - Now + TimeSpan.FromMinutes(1)); // 06:42: the only alarm has passed; still inside ActiveTime

            RaiseSecond();

            await app.LastRun.WaitAsync(Guard);
            Assert.Equal(2, PlannerCalls); // activation + one re-query that still found nothing in the future
            VerifyAppUpdates(Times.Never());
            VerifyNoErrorsLogged();
        }

        [Fact]
        public async Task ExhaustedList_ReQueryFindsALaterService_KeepsCountingDown()
        {
            var app = CreateApp();
            app.ExecuteNow();
            await _delays.NextAsync();
            _time.Advance(Departure - Now + TimeSpan.FromMinutes(1));
            _plannerResult = _ => Task.FromResult(Departures(Departure.AddMinutes(15)));

            RaiseSecond(); // finds nothing, re-queries, keeps the window

            Assert.False(app.LastRun.IsCompleted);
            Assert.Equal(Departure.AddMinutes(15), OnlyDeparture(app));

            RaiseSecond(); // counts down to the later service
            VerifyAppUpdates(Times.Once());
            await app.DisposeAsync();
        }

        [Fact]
        public async Task EndingTheWindow_CancelsTheInFlightQuery_AndIgnoresItsLateResult()
        {
            var app = CreateApp();
            app.ExecuteNow();
            var wait = await _delays.NextAsync();

            var inFlight = new TaskCompletionSource<List<TripSummary>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _plannerResult = token => inFlight.Task.WaitAsync(token);
            wait.Release();
            Assert.True(SpinWait.SpinUntil(() => PlannerCalls == 2, Guard));

            await app.DisposeAsync();

            Assert.True(_plannerTokens.Last().IsCancellationRequested);
            inFlight.TrySetResult(Departures(Departure.AddMinutes(30)));
            Assert.Equal(Departure, OnlyDeparture(app));
            Assert.Equal(0, _delays.Pending);
            VerifyNoErrorsLogged();
        }

        private sealed class ScriptedDelay
        {
            private readonly Channel<Step> _steps = Channel.CreateUnbounded<Step>();

            public int Pending => _steps.Reader.Count;

            public Task Delay(TimeSpan delay, CancellationToken token)
            {
                var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _steps.Writer.TryWrite(new Step(delay, gate));
                return gate.Task.WaitAsync(token);
            }

            public async Task<Step> NextAsync() => await _steps.Reader.ReadAsync().AsTask().WaitAsync(Guard);
        }

        private sealed record Step(TimeSpan Delay, TaskCompletionSource Gate)
        {
            public void Release() => Gate.TrySetResult();
        }
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~TripTimerAppRefreshTests"`
Expected: build FAILS with `CS1061: 'TripTimerApp' does not contain a definition for 'RefreshDelay'` and `CS0117` for `NextRefreshDelay`/`RefreshInterval`/`RetryInterval`.

- [ ] **Step 3: Implement (edits to the post-WS4 `src/api/Apps/TripTimer/TripTimerApp.cs`)**

1. Directly after the line `private volatile IReadOnlyList<TripSummary> _nextDepartures = Array.Empty<TripSummary>();`, add:

```csharp

        /// <summary>How often an active trip timer re-queries departures (CR-25)</summary>
        internal static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(2);

        /// <summary>First retry delay after a failed refresh; doubles per consecutive failure, capped at <see cref="RefreshInterval"/></summary>
        internal static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);

        private readonly object _refreshLock = new();
        private Task<bool>? _refreshInFlight;
        private ScheduledActivation? _refreshActivation;

        /// <summary>True once a query in the current activation returned at least one departure</summary>
        private volatile bool _departuresLoaded;
```

2. In the constructor, after `VisualAlertBuffer = TimeSpan.FromSeconds(20);`, add:

```csharp
            RefreshDelay = (delay, token) => Task.Delay(delay, Clock.TimeProvider, token);
```

3. After the `SetDepartures` method, add:

```csharp
        /// <summary>Waits between refreshes. Tests replace it to step the loop deterministically.</summary>
        internal Func<TimeSpan, CancellationToken, Task> RefreshDelay { get; set; }

        /// <summary>
        /// Re-query departures for <paramref name="activation"/>. Single-flight: concurrent callers share one request.
        /// Returns true when the list was replaced. Never throws; on failure the last good list is kept (CR-25).
        /// </summary>
        internal Task<bool> RefreshDeparturesAsync(ScheduledActivation activation)
        {
            lock (_refreshLock)
            {
                if (_refreshInFlight is { IsCompleted: false } && ReferenceEquals(_refreshActivation, activation))
                {
                    return _refreshInFlight;
                }

                _refreshActivation = activation;
                return _refreshInFlight = RefreshCoreAsync(activation);
            }
        }

        private async Task<bool> RefreshCoreAsync(ScheduledActivation activation)
        {
            try
            {
                // Find the earliest we could get to the train station and query from then
                var earliestDeparture = Clock.Now.Add(Config.TimeToOrigin).Add(Config.TimeToPrepare);

                var departures = await _tripPlanner.GetNextDepartures(
                    Config.StopIdOrigin, Config.StopIdDestination, earliestDeparture, activation.Token);

                if (activation.IsEnded)
                {
                    return false; // a late answer for a window that has already ended
                }

                if (departures.Count == 0)
                {
                    // The service maps error bodies to an empty list, so empty never clears a good list
                    Logger.LogInformation("Trip planner returned no departures; keeping the {Count} already known", NextDepartures.Count);
                    return false;
                }

                SetDepartures(departures);
                _departuresLoaded = true;
                return true;
            }
            catch (OperationCanceledException) when (activation.Token.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Refreshing departures failed; keeping the {Count} already known", NextDepartures.Count);
                return false;
            }
        }

        private async Task RefreshLoopAsync(ScheduledActivation activation)
        {
            var consecutiveFailures = _departuresLoaded ? 0 : 1;

            try
            {
                while (!activation.IsEnded)
                {
                    await RefreshDelay(NextRefreshDelay(consecutiveFailures), activation.Token);
                    consecutiveFailures = await RefreshDeparturesAsync(activation) ? 0 : consecutiveFailures + 1;
                }
            }
            catch (OperationCanceledException) when (activation.Token.IsCancellationRequested)
            {
                // The window ended; nothing to clean up
            }
        }

        internal static TimeSpan NextRefreshDelay(int consecutiveFailures)
        {
            if (consecutiveFailures <= 0)
            {
                return RefreshInterval;
            }

            var backoff = RetryInterval * (1 << Math.Min(consecutiveFailures - 1, 8));
            return backoff < RefreshInterval ? backoff : RefreshInterval;
        }

        /// <summary>
        /// The countdown has nothing to show. If nothing was loaded yet this activation, keep the window open and let the
        /// refresh loop retry. Otherwise re-query once, and end the window only if there is still nothing (CR-25, CR-19).
        /// </summary>
        private async Task EndIfNoDeparturesRemainAsync(ScheduledActivation activation, DateTime tickTime)
        {
            if (!_departuresLoaded)
            {
                return;
            }

            await RefreshDeparturesAsync(activation);

            if (activation.IsEnded || BuildMessage(tickTime) != null)
            {
                return;
            }

            // Deactivation (unsubscribe + AppClear) runs on the thread pool, never on the tick thread
            Logger.LogInformation("No future departures; ending trip timer activation #{Number}", activation.Number);
            activation.Complete();
        }
```

4. Replace the whole `OnActivateAsync` method with:

```csharp
        protected override async Task OnActivateAsync(ScheduledActivation activation)
        {
            Logger.LogInformation("Trip timer activated ({Trigger})", activation.Trigger);

            // A new window starts from nothing: the previous window's list must not count as loaded
            _departuresLoaded = false;
            _nextDepartures = Array.Empty<TripSummary>();

            var message = new AwtrixAppMessage()
                .SetText("Starting trip timer")
                .SetStack(false);

            await Notify(message);

            // Never throws; on failure the list stays empty and the refresh loop retries sooner (CR-25)
            await RefreshDeparturesAsync(activation);
            activation.Token.ThrowIfCancellationRequested();

            _timerService.SecondChanged += ClockTickSecond;

            _ = FireAndLog(() => RefreshLoopAsync(activation), "Refresh departures");
        }
```

5. In `ClockTickSecond`, replace

```csharp
                var message = BuildMessage(e.Time);
                if (message == null)
                {
                    // CR-19: nothing left to show. End this activation; deactivation (unsubscribe + AppClear)
                    // runs on the thread pool, never on the tick thread.
                    Logger.LogInformation("No future departures; ending trip timer activation #{Number}", activation.Number);
                    activation.Complete();
                    return;
                }
```

with

```csharp
                var message = BuildMessage(e.Time);
                if (message == null)
                {
                    await EndIfNoDeparturesRemainAsync(activation, e.Time);
                    return;
                }
```

(If Task 0 Step 1 found no `IClock.TimeProvider`, use `Task.Delay(delay, TimeProvider.System, token)` in step 2.)

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Apps.TripTimer|FullyQualifiedName~Test.Apps.TripTimerAppTests|FullyQualifiedName~ConductorStartupTests|FullyQualifiedName~Test.Apps.AppDisposalTests"`
Expected: PASS, 0 failed. WS4's `NoFutureDepartures_CompletesActivation_WithoutPublishingAnEmptyPayload` still passes; it now makes two planner calls. WS3's `StartAsync_RightDoubleClick_StartsOnlyThatDevicesTripTimerOnce` still sees one `Notify`.

- [ ] **Step 5: Run the full suite and code checks**

Run: `dotnet test`
Expected: 0 failed; only the WS8 `TripSummary.Factory` test is skipped.

Run each check. The expected result follows each arrow.
- `grep -n "WaitAsync(activation.Token)\|LocalDateTime" src/api/Apps/TripTimer/TripTimerApp.cs` → no matches.
- `grep -n "Task.Delay\|Thread.Sleep" test/Test/Apps/TripTimer/TripTimerAppRefreshTests.cs` → no matches.

- [ ] **Step 6: Commit**

```bash
git add src/api/Apps/TripTimer/TripTimerApp.cs test/Test/Apps/TripTimer/TripTimerAppRefreshTests.cs
git commit -m "fix(trip-timer): refresh departures during the window (CR-25)

An active trip timer re-queries every 2 minutes with the activation's token,
keeps the last good list when a query fails and retries after 30 s, 60 s,
then 2 min. A failed query at activation no longer ends the window, and an
exhausted list is re-queried once before the activation completes. Ending
the window cancels any in-flight request and discards late results.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

## Self-review

- **Spec coverage:**
  - CR-38 and CR-28 (model) → T1
  - CR-29, CR-36, CR-26 and CR-40 (stubs) → T2
  - CR-10, CR-27, CR-28 (filter) and the un-skip → T3
  - CR-37 → T4
  - CR-25 → T5
  - README `TZ` → T2
  - WS7 hand-off → spec §9. The plan keeps the options block and the `TransportTime.TryParseQuery` throw site that §9 names.
  - Deferred items (cache removal, cron timezone) are untouched.
- **Placeholder scan:** every code step has full code; no "TBD"/"similar to".
- **Type consistency:**
  - `TripPlannerTestData.CreateService(HttpMessageHandler, ILogger<TripPlannerService>?, string)` (T2) is used in T3 and T4.
  - `StubHttpMessageHandler.RequestUris` is used in T2 and T4.
  - `TransportTime.TryParseApiTime` (T2) is used in T3; `FromTransportWallClock`/`ToTransportZone` (T2) are used in T4.
  - `DepartureMapper.Map(TripRequestResponse?, ILogger)` (T3) is called in T3 and T4.
  - `TripFileCache.DataDirectoryVariable` (T4) appears in spec §9.
  - `RefreshDeparturesAsync(ScheduledActivation) → Task<bool>`, `RefreshDelay` and `NextRefreshDelay(int)` (T5) match the spec §6 table.
