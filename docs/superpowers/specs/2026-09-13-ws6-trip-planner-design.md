# WS6 Trip Planner Robustness and Timezone — Design

- **Date:** 2026-09-13
- **Branch:** `feature/redo`
- **Source:** `code-review.md` → "WS6. Trip planner robustness and timezone"
- **Findings:** CR-10, CR-25, CR-26, CR-27, CR-28, CR-29, CR-36, CR-37, CR-38, CR-40
- **Execution order:** WS4 → WS5 → **WS6** → WS7 → WS8
- **Depends on:**
  - WS1–WS3 (complete): `IClock` over `TimeProvider`, `FireAndLog` (OCE → Debug, other exceptions → Error, never faults), composition root in `Program.AddAwtrixServices` validated by `CompositionRootTests`, Conductor constructs `TripTimerApp` with the singleton `ITripPlannerService`.
  - WS4 (`docs/superpowers/specs/2026-09-13-ws4-scheduled-apps-design.md`, being implemented): `ScheduledActivation` (`Token`, `IsEnded`, `Complete()`, `Number`, `Trigger`), `OnActivateAsync`/`OnDeactivateAsync`, `CurrentActivation`, `LastRun`, `IClock.TimeProvider`, and on `TripTimerApp` `SetDepartures`, `NextDepartures` (volatile snapshot) and `BuildMessage(DateTime) → null`. WS4 §7 hands CR-25 to WS6.
- **Written against:** the post-WS4 code shape. Plan Task 0 verifies it and gives adaptation rules.
- **Followed by:** WS7 (`docs/superpowers/plans/2026-09-13-ws7-config-hardening.md`); §9 lists exactly what WS7 must adapt.
- **Plan:** `docs/superpowers/plans/2026-09-13-ws6-trip-planner.md`
- **Status:** Approved for implementation. The owner was unavailable; the planner made the decisions below and recorded them for review.

---

## 1. Goals

1. **One bad journey, an error body or a missing time never loses the trip-timer window (CR-10).**
2. **Trip queries are correct on any host timezone (CR-26):** the service takes an instant and talks Sydney wall-clock time to TfNSW.
3. **The departure is when the user must leave the configured origin (the first leg, a leading walk included); journeys boarding the same service and cancelled services are dropped (CR-27, CR-28; revised by the 2026-09-14 C1 ruling).**
4. **HTTP plumbing is correct for a long-lived process (CR-29):** no captured `HttpClient`, a 15 s timeout, cancellation from the activation token down to the socket.
5. **`TransportOpenData:BaseUrl` takes effect (CR-36).**
6. **An active trip timer refreshes departures, keeps the last good list on failure, backs off, and re-queries before giving up (CR-25).**
7. **The opt-in trip file cache can never break a lookup (CR-37).**
8. **An unknown enum value from TfNSW does not fail the whole response (CR-38).**
9. **The service is tested through the real generated client and its real serializer settings (CR-40).**

## 2. Non-goals

| Not in WS6 | Owner / reason |
|---|---|
| Removing the trip file cache | Deferred table (owner decision). WS6 hardens it. |
| Configurable timezone for cron/Diurnal, or changing their host-local default | Deferred table. WS6 documents `TZ` only. |
| Reading the TfNSW key / `DATA_DIRECTORY` through `IConfiguration`; startup warnings; `TripTimerAppConfig` validation; 400 on bad controller dates | WS7 (CR-14, CR-23, CR-15). WS6 keeps today's env-var reads and 500 responses. |
| `TripSummary.Factory` ignoring `place` (skipped test in `TripSummaryBehaviorTests`); `TripSummary.ToString` duration; the commented-out writer in `TripPlannerController` | WS8 (CR-43, CR-44). Not trivial to settle here: the intended `Destination` is unspecified. |
| `Microsoft.Extensions.Http.Resilience` / Polly | Not added (D5). |
| A TfNSW timezone config key | Not added (D2). |
| ScheduledApp engine changes | None needed; WS4's seams are used as-is. |

## 3. Constraints

- **Config backward compatibility is mandatory.** Unchanged meaning: `TRANSPORTOPENDATA__APIKEY`, `TransportOpenData:BaseUrl` (now honoured; default stays `https://api.transport.nsw.gov.au/v1/tp`), `AWTRIXSHARP_SETTINGS__DATA_DIRECTORY`, and the TripTimer keys `CronSchedule`, `ActiveTime`, `StopIdOrigin`, `StopIdDestination`, `TimeToOrigin`, `TimeToPrepare`, `ValueMaps`.
- **No new configuration keys.** New code constants only:
  - `TripPlannerService.HttpClientName = "TransportOpenData"`, `TripPlannerService.HttpTimeout = 15 s`
  - `TransportTime.TimeZoneId = "Australia/Sydney"` (Windows fallback `"AUS Eastern Standard Time"`)
  - `TripTimerApp.RefreshInterval = 2 min`, `TripTimerApp.RetryInterval = 30 s`
  - `TripFileCache.RolloverTolerance = 1 h`
- **No package changes.** Target `net10.0`. `Microsoft.Extensions.TimeProvider.Testing` 10.0.0 is already in `test/Test`.
- **Routes, response codes and MQTT/HTTP publish behaviour do not change.**
- **Tests are deterministic:** stub `HttpMessageHandler`, `FakeTimeProvider`, a scripted refresh delay, `TaskCompletionSource` gates. No network, no `Task.Delay`/`Thread.Sleep` waits. `WaitAsync(5 s)` and one bounded `SpinWait.SpinUntil` are failure guards only.
- **No step runs the app.**
- **WS5 isolation:** WS6 touches no Diurnal/Slack/Button/ValueMap files.

## 4. Verified facts (this session)

| Fact | How verified |
|---|---|
| `TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney")` resolves on this Windows machine under .NET 10 (`HasIanaId = true`) and converts DST correctly (2024-10-05T16:30Z → 03:30+11:00). Linux images (`aspnet:10.0`, Debian) ship tzdata. | Scratch console app |
| The NSwag `TripClient` uses its own `JsonSerializerOptions` (case-sensitive, attribute names) and `static partial void UpdateJsonSerializerSettings`. All three fixtures deserialize through the real client over a stub handler. | Scratch app compiling `TripPlannerClient.nswag.cs` |
| `ErrorResponse.json` returned with HTTP 200 deserializes to `Error.Message = "The application calling the API has not been authenticated."` and `Journeys == null`. Returned with 401 the client throws `TripPlannerException<HttpErrorResponse>`. | Scratch app |
| Replacing a leg stop `"type": "stop"` with `"gisPoint"` makes the client throw `TripPlannerException` ("could not be converted to Nullable<TripRequestResponseJourneyLegStopType>"). | Scratch app |
| A property-level `[JsonConverter(typeof(JsonStringEnumConverter))]` beats `options.Converters`, but a `DefaultJsonTypeInfoResolver` modifier setting `JsonPropertyInfo.CustomConverter` overrides it. The lenient converter must accept integers (`interchange.type` is `100`, enum `_100`) and must not call `Utf8JsonReader.Skip()` (throws on non-final blocks during stream deserialisation; use `TrySkip()`). With those rules all fixtures and the `gisPoint`/object-token variants deserialize. | Scratch app |
| `realtimeStatus` is not modelled. A hand-written partial `ICollection<string>? RealtimeStatus` populates: 13 legs in `ComplexTripResponse.json` carry `["MONITORED"]`. | Scratch app |
| `ComplexTripResponse.json` journeys (transit origin estimated times, UTC): J1 05:37:54; J2 starts with a class-100 walk at 05:41:30, boards at 06:00:30; J3 05:53; J4 05:53; J5 06:03; J6 class-100 walk at 06:04, boards 06:23; J7 06:13; J8 06:13. | Script over the fixture + real client |
| `SuccessfulTripResponse.json`'s only leg origin/destination have **no** times; the times are in `stopSequence[0]` (12:01Z estimated) and `stopSequence[^1]` (12:11Z). | Fixture read |
| `CANCELLED` is **not** in repo data. The generated docs list message code `-9999 TRIP_CANCELLED`. | grep; unconfirmable offline |
| `Microsoft.Extensions.Http.Resilience` is not in the local NuGet cache. | `~/.nuget/packages` listing |
| Current TfNSW product classes: 1 train, 5 bus, 100 walk (fixtures); 99 is TfNSW's footpath class (CR-27). `RouteProduct.Class` is `int?`. | Real client output, generated model |

## 5. Design decisions

### D1. `ITripPlannerService` takes an instant and a token

```csharp
Task<StopFinderResponse> FindStops(string query, CancellationToken cancellationToken = default);
Task<List<TripSummary>> GetNextDepartures(string originStopId, string destinationStopId, DateTimeOffset fromWhen, CancellationToken cancellationToken = default);
Task<TripRequestResponse> GetTrips(string originStopId, string destinationStopId, DateTimeOffset fromWhen, CancellationToken cancellationToken = default);
```

- The return type stays `List<TripSummary>` (controller tests and the JSON shape rely on it).
- Moq setups must add `It.IsAny<CancellationToken>()`; the four known call sites are updated in Task 2.

### D2. Sydney time lives in one static helper, `TransportTime`; no config key

`src/api/Services/TripPlanner/TransportTime.cs`:
- `Zone`: `Australia/Sydney`, falling back to `AUS Eastern Standard Time` if the IANA id is unavailable (Windows with invariant globalization).
- `ToTransportZone(DateTimeOffset)`: same instant, Sydney offset.
- `ToQuery(DateTimeOffset) → (itdDate "yyyyMMdd", itdTime "HHmm")`, invariant culture (a Thai-calendar current culture cannot change the year).
- `FromTransportWallClock(DateTime)`: a Sydney wall-clock time → instant.
- `TryParseQuery(string?, out DateTimeOffset)`: offset-less strings are Sydney wall clock; strings with an offset or `Z` keep their instant. Used by `TripPlannerController`.
- `TryParseApiTime(string?, out DateTimeOffset)`: TfNSW timestamps → Sydney-offset instants; null/blank/garbage → false.

**Output offset:** departures are returned with the **Sydney** offset (they used to carry the host-local offset). Instants are unchanged, so `TripTimerApp` comparisons are unaffected, and the `mm` alarm display is identical for whole-hour offsets. Logs and `/api/TripPlanner/departures` JSON now show Sydney times on UTC hosts.

**Rejected:** an optional `TransportOpenData:TimeZone` key (CR-26 suggestion). The API is NSW-only, it would be one more key for WS7 to plumb, and nobody can meaningfully set it to anything else.

### D3. `TripPlannerService` is a singleton that builds NSwag clients per call (CR-29, CR-36)

```csharp
public TripPlannerService(IHttpClientFactory httpClientFactory, IOptions<TransportOpenDataConfig> config, ILogger<TripPlannerService> logger)
```

- Each call: `using var httpClient = _httpClientFactory.CreateClient(HttpClientName)`, then `new TripClient(httpClient)` / `new StopfinderClient(httpClient)`, then `BaseUrl = config.BaseUrl` unless blank (blank keeps the generated default).
- The named client is registered in `Program.AddAwtrixServices`: `Timeout = 15 s`, `Authorization: apikey {ApiKey}` read from `IOptions<TransportOpenDataConfig>` when the client is created. The typed `AddHttpClient<StopfinderClient>`/`<TripClient>` registrations are removed.
- Registration: `AddSingleton<TripPlannerService>()` and `ITripPlannerService` forwarding to it. `TripPlannerController` keeps its concrete dependency.
- Tokens flow to `Request2Async(..., cancellationToken)` / `RequestAsync(..., cancellationToken)`.
- `TransportOpenDataConfig.BaseUrl` default becomes `https://api.transport.nsw.gov.au/v1/tp` (was `/v1`, which never worked). `Program`'s explicit default was already `/v1/tp`, so nothing changes at runtime.
- **HTTP failures are not swallowed by the service.** A non-200 status or transport error still throws (`TripPlannerException`, `HttpRequestException`, `TaskCanceledException` on timeout), so callers can keep their last good list. Only *parsed* error bodies and malformed journeys degrade to "fewer departures" (D4).
- **Rejected:** `PooledConnectionLifetime` on a captured client (still no cancellation, and a transient service captured by a singleton remains misleading).

### D4. `DepartureMapper`: tolerant response → departures (CR-10, CR-27, CR-28)

Revision 2026-09-14: C1 ruling. The trip timer answers "when must I leave the configured origin". A journey whose first leg is a walk from the origin (or its parent station) is a distinct journey, not a duplicate: it departs at the walk start. Only journeys boarding the same transit service are duplicates, and the one with the latest first-leg departure (least waiting) is kept. The original rule (departure = first transit leg; de-dup by instant) made the fixture's J2/J6 count down to a bus 19 minutes' walk away (WS6 review C1).

`internal static class DepartureMapper { List<TripSummary> Map(TripRequestResponse? response, ILogger logger) }`, never throws:

1. `response == null` → Warning, empty. `Error != null` → Warning with `Error.Message`, then continue with `Journeys ?? empty`.
2. Per journey, inside try/catch (Warning, skip):
   - legs = non-null `Legs`; none → Warning, skip.
   - **Transit legs** = legs whose `Transportation.Product.Class` is not 99 or 100 (a leg with no transportation counts as transit). None → Debug, skip (walk-only).
   - **Cancelled:** any transit leg whose `RealtimeStatus` contains a value containing `"CANCEL"` (ordinal, ignore case) → Information, skip. This deliberately matches `CANCELLED`, `TRIP_CANCELLED` and similar, because the exact value is unconfirmed (§4). A false positive would need a status value containing "CANCEL" that does not mean cancelled; none is known.
   - **Departure** = **first leg** (a leading walk included): `Origin.DepartureTimeEstimated` → `Origin.DepartureTimePlanned` → `StopSequence[0].DepartureTimeEstimated` → `StopSequence[0].DepartureTimePlanned`, first value that parses (unparsable strings fall through). None → Warning, skip.
   - **Boarded service** = first transit leg. Its identity (the model carries no trip id; `Properties5` has only `isTTB`/`tripCode`) is the boarding stop (`Origin.Id`, else its place name) plus the transit leg's departure instant (same chain as above). No transit departure time → the journey is never treated as a duplicate.
   - **Arrival** = last leg (including a trailing walk): the same chain on `Destination.Arrival*` and `StopSequence[^1].Arrival*`. None → arrival = departure (the app only uses the departure).
   - **Places:** `DisassembledName ?? Name ?? ""` of the first transit leg origin (where the service is boarded) and the last leg destination.
3. **De-duplicate** journeys with the same boarded service, keeping the one whose first-leg departure is latest (ties keep the first), in the position of the first one seen. API order is otherwise preserved (existing behaviour).
4. **Not done:** skipping journeys whose first transit stop is not `StopIdOrigin`. Stop ids in responses are platform ids (`2000336`) while configs use parent stops, so a match rule would be guesswork; the review marks it optional.

Fixture result for `ComplexTripResponse.json` (Sydney, AEST): 15:37:54, 15:41:30, 15:53, 16:03, 16:04, 16:13. J2 and J6 count from their walk starts at Oatley Station (05:41:30Z, 06:04Z) and show "Macquarie Pl at The Strand" as the boarding place; J4 and J8 board the same service at the same stop and time as J3 and J7 and are dropped.

### D5. No resilience package; retries live in the app

- `Microsoft.Extensions.Http.Resilience` would need a network restore (not in the local cache) and adds a dependency to retry a request the app already re-issues every refresh.
- Timeout: `HttpClient.Timeout = 15 s` (was 100 s). *Revision 2026-09-14 (WS6 review I1):* `HttpClient.Timeout` only bounds the wait for response headers, and the generated clients read the body separately. `TripPlannerService` therefore also wraps every call in a linked CTS that cancels after `HttpTimeout` (driven by an internal `TimeProvider` seam), covering the body read. A timeout while the caller's token is live surfaces as `TimeoutException`, an ordinary failure that `TripTimerApp` logs as a Warning and backs off; a cancelled caller token still surfaces as `OperationCanceledException`.
- Retry: `TripTimerApp`'s refresh loop (D6) with backoff. `FindStops`/`GetTrips` via the controller are interactive and not retried.
- Follow-up (optional, owner): add `AddStandardResilienceHandler()` to the named client once a restore is acceptable; no code shape changes needed.

### D6. `TripTimerApp` refresh loop (CR-25), on WS4's seams

- **Activation** (`OnActivateAsync`): reset `_departuresLoaded = false` and the list to empty, `Notify("Starting trip timer")`, `await RefreshDeparturesAsync(activation)` (never throws), `activation.Token.ThrowIfCancellationRequested()`, subscribe `SecondChanged`, then `_ = FireAndLog(() => RefreshLoopAsync(activation), "Refresh departures")`.
- **`RefreshDeparturesAsync(activation) → Task<bool>`** is single-flight per activation (concurrent callers share one request). It queries from `Clock.Now + TimeToOrigin + TimeToPrepare` with `activation.Token`:
  - non-empty result → `SetDepartures`, `_departuresLoaded = true`, true;
  - empty result → Information, keep the list, false (the service maps error bodies to empty, so empty is not trusted to clear a good list);
  - exception → Warning, keep the list, false; OCE while the token is cancelled → false silently;
  - a result arriving after the activation ended is discarded.
- **Loop:** `while (!activation.IsEnded) { await RefreshDelay(NextRefreshDelay(failures), token); failures = ok ? 0 : failures + 1; }`. It starts with `failures = 1` if the initial query did not load anything. The loop ends by itself when the token cancels; no deactivation code.
- **`NextRefreshDelay(n)`:** 0 → 2 min; n ≥ 1 → `30 s × 2^(n-1)` capped at 2 min (30 s, 60 s, 2 min, 2 min…).
- **Exhaustion (tick with `BuildMessage == null`):**
  - nothing loaded yet this activation → do nothing. The window stays open, the loop retries, and `ActiveTime` bounds it. This fixes "a transient 503 at activation loses the window".
  - otherwise → `await RefreshDeparturesAsync` once, then if `BuildMessage` is still null → `activation.Complete()` (WS4's CR-19 path). If the re-query fails, the window ends: the last known departure has already passed, and hammering a failing API once per second is worse.
- **Seam:** `internal Func<TimeSpan, CancellationToken, Task> RefreshDelay`, defaulting to `Task.Delay(delay, Clock.TimeProvider, token)`. Tests script it, so loop steps are released explicitly without depending on timer registration order on `FakeTimeProvider`.
- **Noise:** `SetDepartures` logs each departure at Information on every successful refresh (≈ 6 lines every 2 min while active). Accepted; WS8 can demote it.

### D7. `TripFileCache` (CR-37)

`internal sealed class TripFileCache(string? directory, ILogger logger)` with `Task<List<TripSummary>?> TryLoadAsync(origin, destination, DateTimeOffset fromWhen, CancellationToken)`:
- Blank directory → null. `TripPlannerService` constructs it per call from `Environment.GetEnvironmentVariable("AWTRIXSHARP_SETTINGS__DATA_DIRECTORY")`, so the existing env var and tests keep working. WS7 swaps the directory source (§9).
- **Stop ids** must match `^[A-Za-z0-9_-]{1,64}$`, otherwise Warning → null. Digits-only (CR-37's suggestion) was rejected: the existing test and WS7's planned test use `originA`/`destB`, and the aim (no path separators or `..`) is met either way.
- **File name** `trip_{origin}_{destination}_{HH}.json`, where `HH` is the **Sydney** hour of `fromWhen`.
- **Any** `IOException`/`UnauthorizedAccessException`/`JsonException`/`NotSupportedException` → Warning → null. JSON `null`, `[]`, or entries missing origin/destination → null. **Null means the caller uses the live API.**
- **Re-dating:** each cached origin wall-clock time (`Time.TimeOfDay`, seconds kept) goes onto the query's Sydney date with that date's Sydney offset. If the result is more than `RolloverTolerance` (1 h) before the query time, it moves to the next day (the post-midnight fix). Destination = re-dated origin + wall-clock travel time (+24 h if negative).
- **Kept as-is:** a cache hit is used instead of live data. That is the feature's purpose (slow TfNSW connections), it is opt-in, and removing that behaviour is the Deferred alternative.
- **`IClock` not needed:** re-dating is relative to the query instant the caller supplies.

### D8. Lenient enums and `realtimeStatus` in hand-written partials (CR-38, CR-28)

`src/transportOpenData/TripPlanner/`:
- **`LenientEnumJson.cs`:**
  - `LenientEnumJson.Apply(JsonSerializerOptions)` adds a resolver modifier. For every object property whose type is `Nullable<enum>` it sets `CustomConverter = LenientNullableEnumConverter<TEnum>`.
  - **Read:** string matching the `EnumMember` value or member name (ignore case), or a defined integer → the value; any other string, number, object or array → `null` (containers via `TrySkip()`).
  - **Write:** the `EnumMember` value, else the name.
  - Non-nullable enum properties keep the generated behaviour (none is on the trip response path we read).
- **`TripPlannerClient.Customizations.cs`:** `static partial void UpdateJsonSerializerSettings(...) => LenientEnumJson.Apply(settings)` for `TripClient`, `StopfinderClient`, `AddinfoClient`, `CoordClient`, `DmClient`; plus `partial class TripRequestResponseJourneyLeg { [JsonPropertyName("realtimeStatus")] ICollection<string>? RealtimeStatus }`.
- **Why not "unknown":** the affected properties are nullable, so `null` needs no new enum member and survives regeneration.
- **Regeneration-safe:** the generated file is not edited.

### D9. Test structure (CR-40)

- **`test/transportOpenData.Tests/TripPlanner/TripClientFixtureTests.cs`:** fixtures through a real `TripClient` over `Helpers/StubHttpMessageHandler.cs`, covering lenient enums, integer enums, `RealtimeStatus` and the 200 error body. The old case-insensitive `TripRequestResponseDeserializationTests` stay (harmless) but are no longer the only guard.
- **`test/Test/Test.csproj`** links `..\transportOpenData.Tests\TestData\*.json` to `TripPlanner\TestData\` in the output (no fixture copies).
- **`test/Test/TripPlanner/TripPlannerTestDoubles.cs`:** `StubHttpMessageHandler`, `StubHttpClientFactory`, and `TripPlannerTestData` (`Fixture`, `ToJson`, `CreateService`, `Stop`/`Leg`/`Journey`/`Response` builders). The two small stub-handler classes are duplicated across the two test assemblies on purpose (no shared test project).
- **Rewritten onto stubs:** `TripPlannerServiceTests` and `TripPlannerControllerTests`, which used to Moq the generated clients (a mocked client never exercises the serializer).
- **New:** `TransportTimeTests`, `DepartureMappingTests`, `TripFileCacheTests`, `TripTimerAppRefreshTests`.
- The skipped `GetNextDepartures_NullEstimatedTime_FallsBackToPlannedTime` is un-skipped in Task 3.

## 6. Interfaces after WS6

| Member | Shape |
|---|---|
| `ITripPlannerService` | D1 |
| `TripPlannerService` ctor | `(IHttpClientFactory, IOptions<TransportOpenDataConfig>, ILogger<TripPlannerService>)` |
| `TripPlannerService.HttpClientName` / `HttpTimeout` | `"TransportOpenData"` / 15 s |
| `TransportTime` | D2 |
| `DepartureMapper.Map(TripRequestResponse?, ILogger)` | internal static |
| `TripFileCache` | `internal sealed`, ctor `(string? directory, ILogger)`, `DataDirectoryVariable`, `TryLoadAsync(...)`, `Redate(TripSummary, DateTimeOffset)` |
| `TripTimerApp` | `internal static RefreshInterval`, `RetryInterval`, `NextRefreshDelay(int)`; `internal Func<TimeSpan, CancellationToken, Task> RefreshDelay`; `internal Task<bool> RefreshDeparturesAsync(ScheduledActivation)` |
| `TripRequestResponseJourneyLeg.RealtimeStatus` | `ICollection<string>?` |
| `LenientEnumJson.Apply`, `LenientNullableEnumConverter<TEnum>` | public, `TransportOpenData.TripPlanner` |
| `TripPlannerController` actions | add `CancellationToken cancellationToken = default`; parse `fromDateTime` with `TransportTime.TryParseQuery` (failure still 500) |

## 7. Behaviour changes visible to users

| Change | Why |
|---|---|
| Trip queries on hosts without `TZ` ask for the right date and time | CR-26 |
| Departure times in logs and `/api/TripPlanner/departures` carry the Sydney offset | D2 |
| `fromDateTime` without an offset means Sydney time (was host-local) | CR-26 |
| A journey that starts with a walk from the origin counts down to the walk start (when to leave the origin); no duplicate alarm for the same boarded service; cancelled services are ignored | CR-27 (C1 ruling), CR-28 |
| An error body, one bad journey or a missing estimate no longer blanks the window | CR-10 |
| Delays that appear during the window are picked up within ~2 min | CR-25 |
| A trip lookup gives up after 15 s instead of 100 s | CR-29 |
| `TransportOpenData:BaseUrl` is applied | CR-36 |
| A broken, empty or unsafe cache file falls back to the API; post-midnight cache entries land on the next day; the cache hour key is the Sydney hour | CR-37 |
| README documents `TZ` for cron/Diurnal | CR-26 |

## 8. Acceptance criteria

### CR-10: malformed responses
1. `ErrorResponse.json` (HTTP 200) → empty list, one Warning containing the API message. *(DepartureMappingTests)*
2. A journey with no parsable departure time is skipped and the other journeys are returned. *(DepartureMappingTests)*
3. Journeys with null legs, empty legs or a null journey entry are skipped. *(DepartureMappingTests)*
4. Estimated → planned fallback for departure and arrival; `SuccessfulTripResponse.json` falls back to `stopSequence` times; unparsable strings fall through. *(DepartureMappingTests, TripPlannerServiceTests un-skipped test)*
5. HTTP 503 still throws `TripPlannerException` from the service. *(DepartureMappingTests)*

### CR-26: timezone
1. `ToQuery` gives Sydney `yyyyMMdd`/`HHmm` for instants in UTC, +10, +05:30 and across both DST transitions, independent of `CultureInfo.CurrentCulture`. *(TransportTimeTests)*
2. `GetTrips` with `2025-08-15T20:22Z` requests `itdDate=20250816&itdTime=0622`. *(TripPlannerServiceTests)*
3. The controller's offset-less `2025-09-01T06:00:00` requests `itdDate=20250901&itdTime=0600`. *(TripPlannerControllerTests)*
4. Returned departures carry the Sydney offset and preserve the API instant. *(TripPlannerServiceTests)*
5. `grep -rn "LocalDateTime\|TimeZoneInfo.Local" src/api/Services/TripPlanner src/api/Apps/TripTimer src/api/Controllers/TripPlannerController.cs` → no matches.

### CR-27: walking legs
Revision 2026-09-14: C1 ruling.

1. `ComplexTripResponse.json` → exactly 15:37:54, 15:41:30, 15:53, 16:03, 16:04, 16:13 (+10:00); the J2 and J6 places are "Macquarie Pl at The Strand"; 16:00:30 and 16:23 do not appear. *(DepartureMappingTests)*
2. A leading walk departs at its own Estimated → Planned time. *(DepartureMappingTests)*
3. Walk-only journeys are skipped. Journeys boarding the same service (same boarding stop and transit departure) collapse to the latest first-leg departure whatever their order, so a walk ahead of a train that is also boarded directly gives neither an early nor a duplicate alarm (CR-27's symptom); equal departures keep the first; the same instant from different stops is kept twice. *(DepartureMappingTests)*

### CR-28: cancellations
1. A transit leg with `realtimeStatus` `CANCELLED`, `TRIP_CANCELLED` or `cancelled` removes its journey; `MONITORED` does not. *(DepartureMappingTests)*
2. `RealtimeStatus` deserializes as `["MONITORED"]` through the real client. *(TripClientFixtureTests)*

### CR-29: HTTP plumbing
1. `TripPlannerService` and `ITripPlannerService` resolve to the same singleton; the named client has a 15 s timeout and an `apikey` Authorization header. *(CompositionRootTests)*
2. Two calls create two named clients (nothing captured). *(TripPlannerServiceTests)*
3. Cancelling the token cancels an in-flight request, including one stalled reading the body. *(TripPlannerServiceTests)*
3a. A response whose body stalls after the headers fails with `TimeoutException` exactly at `HttpTimeout` (FakeTimeProvider), for both `trip` and `stop_finder`. *(TripPlannerServiceTests; revision 2026-09-14, WS6 review I1)*
4. Ending an activation cancels the token the planner received. *(TripTimerAppRefreshTests)*
5. `grep -n "AddHttpClient<TripClient>\|AddHttpClient<StopfinderClient>\|AddTransient<TripPlannerService>" src/api/Program.cs` → no matches.

### CR-36: BaseUrl
1. A configured BaseUrl prefixes `stop_finder` and `trip` requests; a blank BaseUrl uses `https://api.transport.nsw.gov.au/v1/tp`. *(TripPlannerServiceTests)*
2. `new TransportOpenDataConfig().BaseUrl == ".../v1/tp"`. *(TransportOpenDataConfigTests)*

### CR-25: refresh
1. After `RefreshInterval` the list is replaced by the new query result. *(TripTimerAppRefreshTests)*
2. A failed refresh keeps the list; retries wait 30 s then 60 s; success resets to 2 min; no Error logs. *(TripTimerAppRefreshTests)*
3. A failed initial query keeps the window open on ticks; a later successful retry makes the countdown publish. *(TripTimerAppRefreshTests)*
4. On exhaustion the app re-queries once: still nothing → the activation completes (2 planner calls); a later service → the countdown continues. *(TripTimerAppRefreshTests)*
5. After disposal the in-flight request's token is cancelled, a late result is not applied, and no Error is logged. *(TripTimerAppRefreshTests)*
5a. A late result from a superseded activation (planner ignoring the token) is discarded and does not mark the next activation as loaded; ticks queued behind the exhaustion re-query log "No future departures" once. *(TripTimerAppRefreshTests; revision 2026-09-14, WS6 review m1/m2)*
6. `NextRefreshDelay` values for 0, 1, 2, 3, 40 failures. *(TripTimerAppRefreshTests)*
7. WS4's `TripTimerAppTickTests` and WS3's double-click Conductor test still pass.

### CR-37: cache
1. No directory / no file → null (API used). *(TripFileCacheTests)*
2. Invalid JSON, `null`, `[]` → null with a Warning; at service level an invalid file falls back to one API request. *(TripFileCacheTests, TripPlannerServiceTests)*
3. Unsafe stop ids (`../x`, `a/b`, `123\n`) → null. *(TripFileCacheTests)*
4. The hour key is the Sydney hour whatever the caller's offset. *(TripFileCacheTests)*
5. Same-day entries keep wall clock, seconds and place on the query date; post-midnight entries and arrivals roll to the next day; a DST-start date gets +11:00. *(TripFileCacheTests)*

### CR-38: strict enums
1. `gisPoint` or an object token in a leg stop `type` → the response deserializes and `Type == null`; `"stop"` still maps to `Stop`; integer `100` still maps to `_100`. *(TripClientFixtureTests)*

### CR-40: test gaps
1. The service and controller tests contain no `Mock<TripClient>`/`Mock<StopfinderClient>` (`grep -rn "Mock<TripClient>\|Mock<StopfinderClient>" test/Test` → no matches).
2. `grep -n "Skip =" test/Test/TripPlanner/TripPlannerServiceTests.cs` → no matches.

## 9. Hand-off to WS7 (what its plan must adapt)

WS7's plan was written before WS6. After WS6:

1. **`TripPlannerService` constructor** is `(IHttpClientFactory httpClientFactory, IOptions<TransportOpenDataConfig> config, ILogger<TripPlannerService> logger)`.
   - WS7 appends `IOptions<DataSettings>? dataSettings = null` as the last parameter, as planned.
   - The env-var read to replace is in `GetNextDepartures`: `new TripFileCache(Environment.GetEnvironmentVariable(TripFileCache.DataDirectoryVariable), _logger)`. Replace only the argument with `(_dataSettings?.Value ?? new DataSettings().WithEnvironmentFallback()).DataDirectory`. There is no `TryLocalCache` method any more.
2. **WS7's cache test** (Task 2 Step 1) must not use `_mockStopFinderClient`/`_mockTripClient` (they no longer exist). Construct `new TripPlannerService(new StubHttpClientFactory(handler), Options.Create(new TransportOpenDataConfig { BaseUrl = TripPlannerTestData.BaseUrl }), NullLogger<TripPlannerService>.Instance, Options.Create(new DataSettings { DataDirectory = tempDir }))`.
   - Use a Sydney-offset query instant, e.g. `new DateTimeOffset(2025, 1, 1, 8, 0, 0, TimeSpan.FromHours(11))`; the file hour key is the Sydney hour.
   - Its cached 06:30 entry rolls to the next day (more than 1 h before 08:00) but is still returned, so `Assert.Single` + place still holds.
3. **TfNSW options block:** WS6 leaves `services.Configure<TransportOpenDataConfig>(...)` in `Program.AddAwtrixServices` untouched and only replaces the typed-client registrations with the named client. WS7 replaces that block with `AddSettings` as planned. The named client reads `IOptions<TransportOpenDataConfig>.Value.ApiKey` when a client is created, so WS7's configuration source flows through with no further change. `TransportOpenDataConfig.BaseUrl`'s class default is now `/v1/tp` too; WS7's "explicit reads rather than Bind" comment becomes stale but harmless.
4. **Controller dates (WS7 Task 6):** `TripPlannerController` already parses with `TransportTime.TryParseQuery(fromDateTime, out var fromTimestamp)` and currently `throw new FormatException(...)` inside the try (→ 500). WS7 should replace that throw with `return BadRequest(...)` and keep `TransportTime.TryParseQuery` rather than `DateTimeOffset.TryParse(..., AssumeLocal)`, which would reintroduce host-timezone dependence. Its controller tests must build the controller with `new TripPlannerController(TripPlannerTestData.CreateService(handler), NullLogger<TripPlannerController>.Instance)`.
5. **Moq setups of `GetNextDepartures`** need four arguments: `(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())`.
6. **`TripTimerAppConfig` validation:** no collision. WS6 does not modify `TripTimerAppConfig`.

## 10. Risks

- **WS4 drift.** Task 5 depends on WS4's exact names. Task 0 verifies them; if WS4 has not landed, Tasks 1–4 can proceed (Task 2 has an adaptation for the pre-WS4 `ActivateScheduledWork` call) and Task 5 waits.
- **Behavioural blast radius of D4 on real data.** After the C1 ruling, walk-first journeys keep the pre-WS6 departure (the walk start), so the visible change is limited to de-duplication and cancelled services. De-dup keys on boarding stop + transit departure instant because the model has no trip id; two journeys whose realtime estimates for the same service differ by seconds would both survive (they round to the same minute, and the earliest alarm wins).
- **Cancellation matching is heuristic** (§4). If TfNSW signals cancellation only via `isCancelled` on stops or a message code, CR-28 remains partially open. The partial class makes adding a field a one-line change.
- **Env-var tests share process state.** The service-level cache tests set `AWTRIXSHARP_SETTINGS__DATA_DIRECTORY`, as today. They stay in one test class (xUnit runs a class's tests serially). `TripFileCacheTests` passes the directory explicitly and never touches the env var. A developer machine with the variable set could still affect `TripPlannerControllerTests`, as it could before.
- **Refresh vs CR-19 test from WS4.** `NoFutureDepartures_CompletesActivation_WithoutPublishingAnEmptyPayload` now makes two planner calls before completing; its assertions do not count calls, so it still passes.
- **Default HTTP handler lifetime.** `IHttpClientFactory`'s 2-minute handler lifetime applies; nothing to configure.
- **`th-TH` culture availability.** The culture-independence test needs ICU culture data. If a CI image runs with invariant globalization, `new CultureInfo("th-TH")` throws `CultureNotFoundException`. The test would fail loudly rather than pass vacuously; switch it to `ar-SA` or skip under invariant mode if that ever happens.
