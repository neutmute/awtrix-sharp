# WS7 Configuration, Secrets and HTTP-Surface Hardening — Design

- **Date:** 2026-09-13
- **Branch:** `feature/redo`
- **Source:** `code-review.md` → "WS7. Configuration, secrets and HTTP-surface hardening (compatible subset)"
- **Findings:** CR-13, CR-14, CR-15, CR-22, CR-23
- **Depends on:**
  - WS1 (`docs/superpowers/specs/2026-09-13-ws1-runtime-resilience-design.md`): `Program.AddAwtrixServices` composition root, `CompositionRootTests`, interface-only `Conductor` constructor, `ConductorTestHelper`.
  - WS3 (`docs/superpowers/specs/2026-09-13-ws3-conductor-lifecycle-design.md`): per-app creation guard in `Conductor` (D5), registry/`FindApps(type, baseTopic?)`, `ExecuteNow` result mapping, `TripTimerController.TestTimingConfig` returning 404 when no app is running.
  - WS6 (trip planner) may reshape `TripPlannerService` and the TfNSW `HttpClient` registrations. WS7 touches only the options registration and the `DATA_DIRECTORY` read; plan Task 0 adapts names.
- **Execution order:** WS2 → WS3 → WS4 → WS5 → WS6 → **WS7** → WS8. WS7 is written against today's source plus the WS3 design; Task 0 of the plan verifies the assumed shapes.
- **Plan:** `docs/superpowers/plans/2026-09-13-ws7-config-hardening.md`
- **Status:** Approved for implementation. The owner was unavailable, so the planner made the decisions below and recorded them for review.

---

## 1. Goals

1. **Configuration precedence is the .NET default (CR-13).** `appsettings.json` is loaded once, by `WebApplication.CreateBuilder`. `appsettings.{Environment}.json`, user secrets, unprefixed environment variables and the command line override it again. `AWTRIXSHARP_*` still wins over everything.
2. **Secrets and settings are read through `IConfiguration` (CR-14),** with the literal environment variable kept as a fallback:
   - `TransportOpenData:ApiKey` (fallback `TRANSPORTOPENDATA__APIKEY`)
   - `Slack:AppToken` (fallback `AWTRIXSHARP_SLACK__APPTOKEN`)
   - `Slack:UserId` (fallback `AWTRIXSHARP_SLACK__USERID`)
   - `Settings:DATA_DIRECTORY` (fallback `AWTRIXSHARP_SETTINGS__DATA_DIRECTORY`)
   - A startup warning is logged when a `TripTimerApp` is configured without a TfNSW key, or a `SlackStatusApp` without a Slack app token.
3. **App config and ValueMap keys are case-insensitive (CR-22),** so `AWTRIXSHARP_AWTRIX__DEVICES__0__APPS__1__CONFIG__STOPIDORIGIN`, `"cronSchedule"` and `"valueMatcher"` work.
4. **Typed config is parsed with the invariant culture and validated at startup (CR-23).**
   - A missing optional value-type key returns `default(T)` instead of throwing `NullReferenceException`.
   - `ScheduledAppConfig`, `MqttAppConfig` and `TripTimerAppConfig` get `Validate()`.
   - `Conductor` calls `EnsureValid(device)` while creating the app, inside WS3's per-app guard. An invalid app is logged with device, app type and key, and skipped. The other apps still start.
5. **The HTTP surface no longer leaks stack traces, and rejects bad input with 4xx (CR-15).**
   - `UseDeveloperExceptionPage` only in Development; otherwise `UseExceptionHandler` with ProblemDetails.
   - Date/time query parameters are parsed with the invariant culture; bad values return 400.
   - Optional `Swagger:Enabled` (default `true`).
   - Optional `Api:Key`. When set, every request that reaches MVC must send it in `X-Api-Key`; when unset the check is inactive.

## 2. Non-goals

| Not in WS7 | Owner |
|---|---|
| **Mandatory API authentication**, binding to localhost | Deferred (owner decision). WS7 adds only the optional key. |
| **Removing `/Mqtt/publish`** or restricting it to Development | Deferred (owner decision) |
| **Swagger off by default in Production** | Deferred (owner decision). WS7 adds the toggle with default `true`. |
| Distinct app `Name`, Click/DoubleClick exclusivity, removing the trip file cache, configurable timezone | Deferred |
| `ExecuteNow` result → 404/200/500 in `TripTimerController.StartNow` / `MqttRenderController.StartNow`; `TestTimingConfig` 404 | WS3 (CR-07). WS7 adds only the 400 for a bad `departureTime`. |
| Diurnal time-map validation (`"0600"` keys), invalid ValueMap regex warnings, ValueMap setter table / invariant formatting | WS5 (CR-20, CR-34) |
| Cache hardening, `GetNextDepartures(DateTimeOffset)`, TfNSW client `BaseUrl`/timeouts | WS6 |
| `Debug/SlackUserHarvester.cs` env-var reads (`AWTRIXSHARP_SLACK__BOTTOKEN`, `DATA_DIRECTORY`) | WS8 removes the harvester (CR-41 area). Not worth plumbing `IConfiguration` into a static debug tool that is about to be deleted. |
| Dropping `UseHttpsRedirection` / `EXPOSE 8081` | WS8 |
| Rate limiting, CORS, request size limits | Not planned |

## 3. Constraints

- **Hard compatibility constraint.** Every existing `appsettings.json` key and environment variable keeps working with identical meaning:
  - `Awtrix:Devices[].BaseTopic`, `Apps[].Type`, `Apps[].Config.*`, `Apps[].ValueMaps[]`
  - `Mqtt:Host`, `Mqtt:Username`, `Mqtt:Password` (and `AWTRIXSHARP_MQTT__*`)
  - `AWTRIXSHARP_SLACK__APPTOKEN`, `AWTRIXSHARP_SLACK__USERID`
  - `TRANSPORTOPENDATA__APIKEY`, `TransportOpenData:BaseUrl` (default stays `https://api.transport.nsw.gov.au/v1/tp`)
  - `AWTRIXSHARP_SETTINGS__DATA_DIRECTORY`
  - `Logging:*`, `AllowedHosts`
- **Every new key is optional and defaults to today's behaviour:**

  | New key | Env var | Default | Effect when default |
  |---|---|---|---|
  | `Swagger:Enabled` | `AWTRIXSHARP_SWAGGER__ENABLED` | `true` | Swagger JSON and UI served, as today |
  | `Api:Key` | `AWTRIXSHARP_API__KEY` | unset | No API key check, as today |
  | `Slack:AppToken`, `Slack:UserId`, `TransportOpenData:ApiKey`, `Settings:DATA_DIRECTORY` via appsettings/user secrets | (existing env vars already map here) | unset | Literal env var fallback, as today |

- **Unchanged routes, verbs and success responses.** Behaviour changes on error paths only (§7 release notes).
- **Target framework `net10.0`.** No production package changes. One test-only package: `Microsoft.AspNetCore.TestHost` 10.0.0 in `test/Test`.
- **Tests:**
  - All existing tests keep passing, except those that document the bugs being fixed. The plan replaces those explicitly:
    - `AppConfigConvertValueTests.GetConfig_MissingKey_NonNullableValueType_ThrowsNullReferenceException`
    - `TripPlannerControllerTests.GetDepartures_InvalidDateTime_Returns500`
    - `TripPlannerControllerTests.GetTrip_InvalidDateTime_Returns500`
    - the WS3 startup test that expects a `MqttRenderApp` without `CronSchedule` to be *disposed after a failed init* (it is now rejected at creation, so it is never constructed or disposed)
  - No step runs the app against a real broker or clock.

## 4. Design decisions

### D1. Remove the duplicate `appsettings.json` source (CR-13)
- `SetupConfiguration` keeps only `.AddEnvironmentVariables("AWTRIXSHARP_")` and the two `Configure<>` calls. It becomes `internal static` so a test can apply it to a real `WebApplicationBuilder`.
- The resulting order, lowest to highest:
  1. `appsettings.json`
  2. `appsettings.{Environment}.json`
  3. user secrets (Development only, when the entry assembly has a `UserSecretsId`)
  4. unprefixed environment variables (for example `TRANSPORTOPENDATA__APIKEY`)
  5. command-line arguments
  6. `AWTRIXSHARP_*` environment variables (prefix stripped)
- The second file watcher on `appsettings.json` goes away.

### D2. One place resolves settings; the literal env var is the last fallback (CR-14)
- `Program.AddAwtrixServices` calls a new private `AddSettings(services, configuration)` first, so `CompositionRootTests` covers it.
- **TransportOpenData:** explicit key reads, not `Bind(GetSection(...))`:
  - `ApiKey = FirstNonBlank(configuration["TransportOpenData:ApiKey"], env TRANSPORTOPENDATA__APIKEY) ?? ""`
  - `BaseUrl = FirstNonBlank(configuration["TransportOpenData:BaseUrl"]) ?? "https://api.transport.nsw.gov.au/v1/tp"`
  - Why not `Bind`: `TransportOpenDataConfig.BaseUrl` defaults to `.../v1` in the class, while the service has always used `.../v1/tp`. Binding would silently switch the default. Two explicit reads keep today's values exactly.
  - Blank-string `BaseUrl` now falls back to the default instead of being used as `""`. A blank URL never worked, so this is a fix, not a break.
- **`SlackSettings`** (`Domain/SlackSettings.cs`, `AppToken`, `UserId`): `AddOptions<SlackSettings>().Bind("Slack").PostConfigure(WithEnvironmentFallback)`.
- **`DataSettings`** (`Domain/DataSettings.cs`, `DataDirectory`): configured from the literal key `Settings:DATA_DIRECTORY` (the historical key name is not a valid C# property name), then `PostConfigure(WithEnvironmentFallback)`.
- **Consumers take the options as an optional last constructor parameter** (`IOptions<T>? x = null`). DI always supplies it. When it is null (existing tests, hand-built instances), the consumer uses `new T().WithEnvironmentFallback()`, which is exactly today's env-var-only behaviour. This keeps every existing `new SlackConnector(logger)` / `new TripPlannerService(a, b, c)` call compiling.
  - `SlackConnector`: `internal string? ResolveAppToken()`; `ExecuteAsync` uses it. The "disabled" warning names both the key and the env var.
  - `TripPlannerService`: the cache folder comes from `DataSettings.DataDirectory`.
  - `Conductor`: when a `SlackStatusApp` config's `SlackUserId` is blank, the factory copies `SlackSettings.UserId` into the (cloned) typed config. `SlackStatusApp` keeps its `Get("SlackUserId", "AWTRIXSHARP_SLACK__USERID")` call, so the literal env var remains the final fallback. `AppConfigKeys.Get(key, envVar)` is unchanged.
- **Precedence of the value itself is unchanged:** an explicit app-level `SlackUserId` still wins; the environment variable (now via `Slack:UserId`) is used only when it is blank.
- **Startup warnings:** `Domain/ConfigurationWarnings.Get(AwtrixConfig, TransportOpenDataConfig, SlackSettings)` returns messages; `Program.LogStartup` logs each at Warning. Type matching is ordinal, like the Conductor factory. Warnings never stop startup.

### D3. Case-insensitive dictionaries (CR-22)
- `AppConfigKeys()` and `ValueMap()` call `base(StringComparer.OrdinalIgnoreCase)`. Both `Clone()` methods construct through that constructor, so clones keep the comparer.
- The configuration binder creates or fills these instances through the parameterless constructor, so bound config inherits the comparer.
- **Out of scope here:** `ValueMap.Decorate` compares `kvp.Key == "ValueMatcher"` ordinally. A lowercase `valueMatcher` key is passed to the reflection setter lookup, finds no `SetValueMatcher`, and is ignored. That is harmless, and WS5 rewrites `Decorate`.
- Collection initialisers that add two keys differing only by case now throw. `IConfiguration` cannot produce that, and no test or config does it.

### D4. Invariant culture, `default(T)` for missing values (CR-23)
- `ConvertValue` passes `CultureInfo.InvariantCulture` to every `Parse` (int, long, double, decimal, DateTime, TimeSpan).
- A null value, or a whitespace-only value for any non-string target, returns null. `GetConfig<T>` returns `default(T)` for null (so `TimeSpan.Zero`, `0`, `false`, `null` for nullables) instead of casting null.
- `SetConfig<T>` writes `IFormattable` values with the invariant culture, so a round trip on a de-DE host is stable. `Config[key] = ...` replaces the ContainsKey/Add pair (same behaviour).
- A *malformed* value still throws `FormatException` on property access. Validation (D5) ensures that cannot happen for the validated keys of a running app.

### D5. `Validate()` / `EnsureValid(device)` and where it runs (CR-23)
- `AppConfig`:
  - `public virtual IReadOnlyList<string> Validate()` returns an empty list. Each entry is `"{Key}: {reason}"`.
  - `public void EnsureValid(string? device)` throws `AppConfigValidationException(appType, device, errors)` when `Validate()` is non-empty. Message: `Invalid configuration for app '{Type}' on device '{device}': {error1}; {error2}`.
  - Protected helpers `ValidateRequired(errors, key)` and `ValidateTimeSpan(errors, key, required, mustBePositive)`.
- **Rules:**

  | Config | Key | Rule |
  |---|---|---|
  | `ScheduledAppConfig` | `CronSchedule` | required; `CrontabSchedule.TryParse` (same 5-field options as `ScheduledApp.Initialize`) must succeed |
  | `ScheduledAppConfig` | `ActiveTime` | required; invariant `TimeSpan`; `> 00:00:00` |
  | `MqttAppConfig` | `ReadTopic` | required |
  | `TripTimerAppConfig` | `StopIdOrigin`, `StopIdDestination` | required |
  | `TripTimerAppConfig` | `TimeToOrigin`, `TimeToPrepare` | optional (missing → `00:00:00`); when present, invariant `TimeSpan`, `>= 00:00:00` |
  | `AppConfig` (Diurnal, Slack) | — | no rules (Diurnal keys are WS5) |

- **Decision: `ActiveTime` is required.** Today a missing `ActiveTime` starts cleanly and then throws at the first cron wake-up, after which the app never reschedules (the CR-23 failure scenario). No working configuration relies on omitting it. After WS4, manual runs also honour `ActiveTime`, so `00:00:00` would end every run immediately; it is rejected too.
- **Decision: `TimeToOrigin`/`TimeToPrepare` are optional with default zero.** Zero is a meaningful value (the stop is next door, no preparation), and the review asks for `default(T)` on missing optional values.
- **Where it runs:** in `Conductor.AppFactory`, directly after `appConfig.As<T>()` and before the app constructor, for `TripTimerApp`, `MqttRenderApp` and `MqttClockRenderApp`. Not for the synthetic `ButtonApp` config (`AppConfig.Empty().As<MqttAppConfig>()` has no cron). The exception propagates into WS3's per-app creation guard (WS3 D5), which logs `Failed to create app {AppType} on device {Device}: {Reason}; skipping` with the message above, and continues with the next app.
- **Why at creation, not in `InitAsync`:** WS3's lifecycle contract (§5) allows validation in either place. Creation-time rejection means an invalid app is never constructed, never touches the display, and needs no dispose.

### D6. Exception handling by environment (CR-15)
- `Program` is split into `public static void AddHttpSurface(IServiceCollection, IConfiguration)` (controllers, `AddProblemDetails`, `ApiSettings` options, Swagger generation) and `public static void ConfigureHttpPipeline(WebApplication)`. `Main` calls both; tests host them on `TestServer`.
- Pipeline order:
  1. `IsDevelopment()` → `UseDeveloperExceptionPage()`; otherwise `UseExceptionHandler()` (ProblemDetails via `AddProblemDetails`, status 500, no exception text).
  2. Swagger (if enabled, D7)
  3. `UseHttpsRedirection()` (unchanged; WS8 removes it)
  4. `ApiKeyMiddleware` (D8)
  5. `MapControllers()`
- The Docker image runs with no `ASPNETCORE_ENVIRONMENT`, so it is Production and no longer returns stack traces. `launchSettings.json` sets Development, so local debugging is unchanged.

### D7. `Swagger:Enabled`
- Read once at pipeline build: blank → `true`; `bool.TryParse` succeeds → that value; anything else → `true` plus a Warning log. An invalid value must not stop the host or silently hide the UI the owner relies on.
- Swagger generation is always registered; only `UseSwagger`/`UseSwaggerUI` are skipped. Disabled → `/swagger/*` returns 404 (or 401 when `Api:Key` is set, because the request then reaches the key check).

### D8. Optional `Api:Key`
- `Domain/ApiSettings` (`Key`), bound from section `Api`, read per request through `IOptionsMonitor` so a reloaded `appsettings.json` takes effect without restart.
- `Middleware/ApiKeyMiddleware`:
  - Key blank → pass through (today's behaviour).
  - Key set → header `X-Api-Key` must match. Both values are trimmed (Docker secrets often carry a trailing newline), SHA-256 hashed and compared with `CryptographicOperations.FixedTimeEquals`.
  - Mismatch → 401 `application/problem+json` (`title: "Missing or invalid API key"`) and a Warning log with method, path and remote IP (never the supplied key).
- **Scope:** all requests that pass the Swagger middleware, regardless of verb. That includes `GET /api/TripPlanner/*`, which spends the owner's TfNSW quota. Swagger JSON/UI stay reachable because they short-circuit earlier in the pipeline, so the UI can be used to enter the key.
- **Header only.** No query-string key: it would end up in logs and browser history.
- When `Api:Key` is set at startup, Swagger generation adds an `ApiKey` header security scheme and a global requirement, so "Try it out" sends the header.

### D9. Controller input validation (CR-15)
- `TripTimerController.TestTimingConfig`: `DateTimeOffset.TryParse(departureTime, InvariantCulture, AssumeLocal)`; failure → `BadRequest(new { message })`. The not-running case stays WS3's 404 (`FirstOrDefault`; the plan includes it if WS3 did not land it).
- `TripPlannerController.GetDepartures` / `GetTrip`: `DateTime.TryParse(fromDateTime, InvariantCulture, None)` before the `try`; failure → `BadRequest(new { message })`. Upstream failures stay 500 as today.
- Response body shape `{ message }` matches WS3 D6.
- Missing required query parameters are already rejected with 400 by `[ApiController]` (non-nullable reference types are implicitly required); no change.

## 5. Acceptance criteria

### CR-13 precedence
1. A command-line value overrides `appsettings.json`. *(ConfigurationPrecedenceTests)*
2. `appsettings.Development.json` overrides `appsettings.json` in Development. *(ConfigurationPrecedenceTests)*
3. An `AWTRIXSHARP_*` variable still overrides the command line. *(ConfigurationPrecedenceTests)*
4. Exactly one `appsettings.json` JSON source is registered. *(ConfigurationPrecedenceTests)*

### CR-14 secrets
1. `TransportOpenData:ApiKey`, `TransportOpenData:BaseUrl`, `Slack:AppToken`, `Slack:UserId` and `Settings:DATA_DIRECTORY` from `IConfiguration` reach `IOptions<TransportOpenDataConfig>`, `IOptions<SlackSettings>` and `IOptions<DataSettings>`. *(SettingsResolutionTests)*
2. With no configuration, the TfNSW key equals the literal `TRANSPORTOPENDATA__APIKEY` value (or `""`) and `BaseUrl` is `.../v1/tp`. *(SettingsResolutionTests)*
3. `SlackConnector.ResolveAppToken()` returns the options value; with no options it returns the literal env var. *(SettingsResolutionTests)*
4. A blank app-level `SlackUserId` is filled from `SlackSettings.UserId`; a non-blank one is kept. *(ConductorSlackSettingsTests)*
5. `TripPlannerService` uses the cache directory from `DataSettings` with the env var unset; the existing env-var cache tests still pass. *(TripPlannerServiceTests)*
6. `ConfigurationWarnings.Get` warns for TripTimerApp without key and SlackStatusApp without token, and returns nothing otherwise. *(ConfigurationWarningsTests)*

### CR-22 case
1. `AppConfigKeys`/`ValueMap` lookups ignore case, including after `Clone()`. *(ConfigKeyCaseTests)*
2. Binding `Config:STOPIDORIGIN`, `Config:cronSchedule` and `ValueMaps:0:valueMatcher` reaches `TripTimerAppConfig.StopIdOrigin`, `ScheduledAppConfig.CronSchedule` and `ValueMap.IsMatch`. *(ConfigKeyCaseTests)*

### CR-23 typed config
1. Under `de-DE`, `GetConfig<double>("3.14") == 3.14`, and `SetConfig(3.5)` stores `"3.5"`. *(AppConfigConvertValueTests)*
2. A missing `TimeSpan` key returns `TimeSpan.Zero`. *(AppConfigConvertValueTests)*
3. `Validate()` rules in D5 each produce an error naming the key; the shipped `appsettings.json` app configs validate clean. *(AppConfigValidationTests)*
4. `EnsureValid` throws `AppConfigValidationException` whose message names app type, device and key. *(AppConfigValidationTests)*
5. `Conductor.StartAsync` skips a `TripTimerApp` missing `StopIdOrigin` and a `MqttRenderApp` with `ActiveTime: "abc"` (no `AppClear` for them) while a `DiurnalApp` on the same device runs; valid scheduled apps are registered. *(ConductorConfigValidationTests)*

### CR-15 HTTP surface
1. Production: an unhandled exception returns 500 `application/problem+json` without the exception message. Development: the response contains it. *(HttpPipelineTests)*
2. Swagger JSON is served by default and with an invalid `Swagger:Enabled`; `Swagger:Enabled=false` → 404. *(HttpPipelineTests)*
3. `Api:Key` unset → requests pass without a header. Set → missing/wrong header 401 problem+json, correct header 200, Swagger JSON still served and documents `X-Api-Key`. *(HttpPipelineTests)*
4. `TestTimingConfig("not-a-date")` → `BadRequestObjectResult`. `GetDepartures`/`GetTrip` with `"not-a-date"` → `BadRequestObjectResult`. `"01/02/2025 06:00"` under en-AU is sent to TfNSW as `20250102` (invariant). *(TripTimerControllerTests, TripPlannerControllerTests)*

## 6. Testing strategy
- TDD per task: failing test (a compile error counts) → implement → filtered run → full `dotnet test` with 0 failed → commit.
- Precedence tests build a real `WebApplicationBuilder` over a temp content root and call `Program.SetupConfiguration`. They use unique `WS7TEST`-style keys and restore any environment variable they set.
- Pipeline tests host `AddHttpSurface`/`ConfigureHttpPipeline` on `TestServer` with in-memory configuration and two minimal endpoints (`/test/ping`, `/test/boom`). No Awtrix services, broker, or network.
- Conductor tests use `ConductorTestHelper` over Moq interfaces (WS1/WS3 pattern).
- Culture tests set `CultureInfo.CurrentCulture` inside try/finally.
- No manual smoke test that runs the app.

## 7. Release notes (to copy into the release / PR description)

**Configuration precedence corrected.** Previously `appsettings.json` was loaded a second time after user secrets, `appsettings.{Environment}.json`, plain environment variables and command-line arguments, so it silently overrode them (only `AWTRIXSHARP_*` variables still won). It is now loaded once, in the standard .NET order (lowest → highest): `appsettings.json` → `appsettings.{Environment}.json` → user secrets (Development) → environment variables → command line → `AWTRIXSHARP_*` environment variables.
- **Who is affected:** anyone who set the same key both in `appsettings.json` and in user secrets, `appsettings.{Environment}.json`, an unprefixed environment variable (e.g. `Awtrix__Devices__0__BaseTopic`) or `--Key=value`. The override now takes effect. In this repository `appsettings.Development.json` only contains `Logging`, so the visible change is that Development log levels apply.
- **Not affected:** `AWTRIXSHARP_*` variables and `TRANSPORTOPENDATA__APIKEY` behave exactly as before.

**Secrets can live in user secrets or appsettings.** `TransportOpenData:ApiKey`, `Slack:AppToken`, `Slack:UserId` and `Settings:DATA_DIRECTORY` are now read from configuration; the existing environment variables still work and are also honoured as a final fallback. Startup logs a warning when a TripTimerApp has no TfNSW key or a SlackStatusApp has no Slack token.

**App config keys are case-insensitive.** `CronSchedule`, `cronSchedule` and `CRONSCHEDULE` are the same key, so `AWTRIXSHARP_AWTRIX__DEVICES__0__APPS__1__CONFIG__STOPIDORIGIN`-style overrides now apply.

**Invalid app configuration is reported at startup.** A scheduled app with a missing or invalid `CronSchedule` or `ActiveTime`, a MqttRender app without `ReadTopic`, or a TripTimer app without `StopIdOrigin`/`StopIdDestination` is now logged (naming device, app and key) and skipped at startup, instead of starting and failing hours later. `TimeToOrigin`/`TimeToPrepare` may now be omitted (treated as `00:00:00`). Values are parsed with the invariant culture (`00:30:00`, `3.14`); a config that relied on a host-culture format such as `3,14` must be changed.

**HTTP errors.** Outside Development, unhandled errors return a generic RFC 7807 ProblemDetails 500 instead of a stack trace. Invalid `fromDateTime`/`departureTime` values return 400 instead of 500, and are parsed with the invariant culture (use ISO `yyyy-MM-ddTHH:mm`; `dd/MM/yyyy` is no longer interpreted by host culture).

**New optional settings.** `Swagger:Enabled` (`AWTRIXSHARP_SWAGGER__ENABLED`, default `true`). `Api:Key` (`AWTRIXSHARP_API__KEY`, default unset): when set, all API calls must send `X-Api-Key: <key>`; Swagger UI remains reachable and offers an "Authorize" box.

## 8. Risks
- **Upstream drift.** WS3 (Conductor factory/guard, controllers, `ConductorTestHelper`) and WS6 (TripPlannerService constructor and cache location, `GetNextDepartures` parameter type) may differ from the assumed shapes. Plan Task 0 greps each and says how to adapt.
- **WS3 test conflict.** WS3's startup test expecting a disposed `MqttRenderApp` without `CronSchedule` must change to "never constructed" (Task 4 does this).
- **Test-only package restore.** `Microsoft.AspNetCore.TestHost` 10.0.0 is not in the local NuGet cache; `dotnet test` needs network access once. If restore is impossible, stop Task 5 and report rather than substituting a Kestrel-on-loopback harness.
- **`ActiveTime` now required.** A config that omitted it and was only ever triggered manually (button/API) stops starting. That app already failed at its first cron wake-up; the release note covers it.
- **Api key scope.** Setting `Api:Key` breaks existing unauthenticated callers (scripts, openHAB rules). That is opt-in by definition; the note says so.
- **Environment-variable tests.** Tests that set process environment variables use unique names or run inside a single test class (xUnit serialises a class), and restore the previous value.
- **Merge churn with WS5** in `ValueMap.cs` (constructor only) and **WS8** in `Program.cs` (`UseHttpsRedirection`). Both are small, local edits.
