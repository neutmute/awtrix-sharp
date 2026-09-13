# WS7 Configuration, Secrets and HTTP-Surface Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Standard .NET config precedence, secrets via `IConfiguration` with env-var fallback, case-insensitive app config keys, invariant-culture typed config validated at startup, and an HTTP surface that hides stack traces outside Development, returns 400 for bad dates, and offers optional `Swagger:Enabled` and `Api:Key` settings.

**Architecture:**
- `Program` loses its duplicate `appsettings.json` source. A new `AddSettings` inside `AddAwtrixServices` resolves `TransportOpenDataConfig`, `SlackSettings` and `DataSettings` from `IConfiguration`, with the literal env var as a `PostConfigure` fallback. Consumers take the options as an optional last constructor parameter.
- `AppConfigKeys`/`ValueMap` use `StringComparer.OrdinalIgnoreCase`. `AppConfig` parses with the invariant culture and exposes `Validate()`/`EnsureValid(device)`. `Conductor.AppFactory` calls `EnsureValid` inside WS3's per-app creation guard.
- `Program` is split into `AddHttpSurface` + `ConfigureHttpPipeline`, which are hosted on `TestServer` in tests. `ApiKeyMiddleware` enforces the optional key.

**Tech Stack:** .NET 10, ASP.NET Core, Swashbuckle 9.0.x (Microsoft.OpenApi 1.x), NCrontab 3.3.3, xUnit 2.9, Moq 4.20, Microsoft.AspNetCore.TestHost 10.0.0 (test only, new)

**Spec:** `docs/superpowers/specs/2026-09-13-ws7-config-hardening-design.md`

## Global Constraints

- **Prerequisite:** WS1-WS6 are fully committed and `git status --short` is clean. Do not start while another agent is committing.
- **Hard compatibility:** every existing `appsettings.json` key and env var keeps identical meaning: `Awtrix:*`, `Mqtt:*`, `AWTRIXSHARP_*`, `TRANSPORTOPENDATA__APIKEY`, `TransportOpenData:BaseUrl` (default `https://api.transport.nsw.gov.au/v1/tp`), `AWTRIXSHARP_SETTINGS__DATA_DIRECTORY`, `AWTRIXSHARP_SLACK__APPTOKEN`, `AWTRIXSHARP_SLACK__USERID`.
- **New keys are optional and default to today's behaviour:** `Swagger:Enabled` default `true`; `Api:Key` unset means no check.
- **Out of scope (Deferred):** mandatory auth, removing or gating `/Mqtt/publish`, Swagger off by default. Do not implement them.
- **Package changes:** only `Microsoft.AspNetCore.TestHost` `10.0.0` in `test/Test/Test.csproj`. No production package changes.
- **No step runs the app** against a real broker or clock.
- **TDD per task:**
  1. Write the failing test.
  2. Run it filtered; it fails (a compile error counts).
  3. Implement.
  4. Run it filtered; it passes.
  5. Run the full `dotnet test` with 0 failed.
- **Commits:** explicit `git add <paths>`, never `-A` or `.`. Every commit message ends with exactly:
  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt
  ```
- Run every command from the repo root `C:\CodeMine\awtrix-sharp`.

## File map

| File | Responsibility | Task |
|---|---|---|
| `src/api/Program.cs` | remove duplicate JSON source (T1); `AddSettings`, startup warnings (T2); `AddHttpSurface`/`ConfigureHttpPipeline`/Swagger toggle (T5) | 1, 2, 5 |
| `test/Test/Configuration/ConfigurationPrecedenceTests.cs` | precedence | 1 |
| `src/api/Domain/SlackSettings.cs`, `src/api/Domain/DataSettings.cs` | settings + env fallback | 2 |
| `src/api/Domain/ConfigurationWarnings.cs` | startup warnings | 2 |
| `src/api/HostedServices/SlackConnector.cs` | token from options | 2 |
| `src/api/HostedServices/Conductor.cs` | SlackUserId from options (T2); `EnsureValid` (T4) | 2, 4 |
| `src/api/Services/TripPlanner/TripPlannerService.cs` | cache dir from options | 2 |
| `test/Test/Configuration/SettingsResolutionTests.cs`, `test/Test/Configuration/ConfigurationWarningsTests.cs`, `test/Test/HostedServices/ConductorSlackSettingsTests.cs` | new | 2 |
| `test/Test/HostedServices/ConductorTestHelper.cs` | `slackSettings` parameter | 2 |
| `test/Test/TripPlanner/TripPlannerServiceTests.cs` | options cache test | 2 |
| `src/api/Apps/Configs/AppConfigKeys.cs`, `src/api/Apps/Configs/ValueMap.cs` | ignore-case comparer | 3 |
| `test/Test/Configs/ConfigKeyCaseTests.cs` | new | 3 |
| `src/api/Apps/Configs/AppConfig.cs`, `ScheduledAppConfig.cs`, `AppConfigValidationException.cs` (new), `src/api/Apps/MqttRender/MqttAppConfig.cs`, `src/api/Apps/TripTimer/TripTimerAppConfig.cs` | invariant parse, validation | 4 |
| `test/Test/Configs/AppConfigValidationTests.cs` (new), `test/Test/Configs/AppConfigConvertValueTests.cs`, `test/Test/HostedServices/ConductorConfigValidationTests.cs` (new), `test/Test/HostedServices/ConductorStartupTests.cs` (WS3) | tests | 4 |
| `src/api/Domain/ApiSettings.cs`, `src/api/Middleware/ApiKeyMiddleware.cs` | optional key | 5 |
| `test/Test/Test.csproj`, `test/Test/Http/HttpPipelineTests.cs` | TestHost + pipeline tests | 5 |
| `src/api/Controllers/TripTimerController.cs`, `src/api/Controllers/TripPlannerController.cs` | 400 on bad dates | 6 |
| `test/Test/Apps/TripTimer/TripTimerControllerTests.cs`, `test/Test/TripPlanner/TripPlannerControllerTests.cs` | tests | 6 |

---

### Task 0: Verify WS1-WS6 interfaces as assumed

**Files:** none modified.

- [ ] **Step 1: Check the working tree and history**

Run: `git status --short` → expect clean.
Run: `git log --oneline -30` → expect WS2, WS3, WS4, WS5 and WS6 commits after `ab4bd03`.

- [ ] **Step 2: Composition root (WS1)**

Run: `grep -n "public static void AddAwtrixServices\|private static void SetupConfiguration\|AddJsonFile\|TRANSPORTOPENDATA__APIKEY\|RegisterSwagger\|UseDeveloperExceptionPage" src/api/Program.cs`
Expected:
- `public static void AddAwtrixServices(IServiceCollection services, IConfiguration configuration)`
- `SetupConfiguration` still contains `.AddJsonFile("appsettings.json", ...)`
- `services.Configure<TransportOpenDataConfig>(config => { config.ApiKey = Environment.GetEnvironmentVariable("TRANSPORTOPENDATA__APIKEY") ...` (WS6 may have moved the HttpClient registrations, but this options block should still exist)
- `UseDeveloperExceptionPage` is still unconditional.

If WS6 moved the TfNSW options block elsewhere (for example an `AddTripPlanner` extension), apply Task 2 Step 3's `AddOptions<TransportOpenDataConfig>` block **in place of that block**, wherever it lives.

- [ ] **Step 3: Conductor per-app guard and registry (WS3)**

Run: `grep -n "AddIfCreated\|IAwtrixApp? AppFactory\|Failed to create app\|FindApps\|public Conductor(\|AppExecutionResult\|RegisterApp\|case AppNames" src/api/HostedServices/Conductor.cs`
Expected:
- `AppFactory` returns `IAwtrixApp?` and is called inside a try/catch that logs `Failed to create app {AppType} on device {Device}: {Reason}; skipping` (WS3 D5).
- `FindApps(string appType, string? baseTopic = null)` exists.
- `case AppNames.TripTimerApp`, `MqttRenderApp`, `MqttClockRenderApp`, `SlackStatusApp` branches each call `appConfig.As<...>()`.

**If the factory call is NOT inside a per-app try/catch, stop.** WS3 is incomplete, and CR-23 validation would crash startup.

Run: `grep -n "public static Conductor Create" -A 12 test/Test/HostedServices/ConductorTestHelper.cs`
Record the parameter list. Task 2 adds `SlackSettings? slackSettings = null` as the **last** parameter.

Run: `grep -rn "CronSchedule" test/Test/HostedServices/ConductorStartupTests.cs`
Record the name of the WS3 test that uses a `MqttRenderApp` without `CronSchedule` and verifies `Dismiss(...)` `Times.Once`. Task 4 Step 6 changes it.

Run: `grep -n "Task InitAsync\|StartAsync" src/api/Interfaces/IAwtrixApp.cs src/api/HostedServices/Conductor.cs`
Expected: `Task InitAsync();` and `public async Task StartAsync(`.

- [ ] **Step 4: Controllers' result mapping (WS3)**

Run: `grep -n "FirstOrDefault\|NotFound\|DateTimeOffset.Parse\|ToActionResult" src/api/Controllers/TripTimerController.cs`
Expected: `TestTimingConfig` uses `FirstOrDefault` and returns `NotFound(...)`, and still calls `DateTimeOffset.Parse(departureTime)`.

Run: `grep -n "DateTime.Parse\|DateTimeOffset.Parse\|GetNextDepartures\|GetTrips" src/api/Controllers/TripPlannerController.cs`
Record whether WS6 changed `GetNextDepartures`/`GetTrips` to take `DateTimeOffset`. If it did, Task 6 parses with `DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var fromTimestamp)` instead of `DateTime.TryParse`, and the en-AU test still expects `itdDate "20250102"`.

- [ ] **Step 5: TripPlannerService config (WS6)**

Run: `grep -rn "DATA_DIRECTORY" src/api --include=*.cs`
Expected: `Services/TripPlanner/TripPlannerService.cs` (and `Debug/SlackUserHarvester.cs`, which is out of scope). If WS6 moved the cache read into another class (for example `TripCache`), apply Task 2's `DataSettings` change to **that** class's constructor and call site instead, and put the options test in that class's test file.

Run: `grep -n "public TripPlannerService(" -A 6 src/api/Services/TripPlanner/TripPlannerService.cs`
Record the constructor. Task 2 appends `IOptions<DataSettings>? dataSettings = null` as the last parameter.

Run: `grep -n "GetNextDepartures(" src/api/Interfaces/ITripPlannerService.cs`
Record the parameter type (`DateTime` or `DateTimeOffset`) for Task 2's cache test.

- [ ] **Step 6: Config classes and ValueMap (WS5)**

Run: `grep -n "public AppConfigKeys()\|public ValueMap()\|: Dictionary<string, string>" src/api/Apps/Configs/AppConfigKeys.cs src/api/Apps/Configs/ValueMap.cs`
Run: `git log --oneline -- src/api/Apps/Configs/AppConfig.cs src/api/Apps/Configs/ScheduledAppConfig.cs src/api/Apps/TripTimer/TripTimerAppConfig.cs src/api/Apps/MqttRender/MqttAppConfig.cs`
Expected: no WS2-WS6 commits touch these four files. If one did, merge its change into the full-file replacements in Task 4 rather than overwriting it.

Run: `grep -n "CrontabSchedule.Parse" src/api/Apps/ScheduledApp.cs`
Expected: `CrontabSchedule.Parse(Config.CronSchedule)` with no options. If WS4 added `ParseOptions`, pass the same options to `CrontabSchedule.TryParse` in Task 4.

- [ ] **Step 7: Baseline**

Run: `dotnet test`
Expected: 0 failed. Record the passed/skipped counts.

---

### Task 1: Load `appsettings.json` once — standard precedence (CR-13)

**Files:**
- Create: `test/Test/Configuration/ConfigurationPrecedenceTests.cs`
- Modify: `src/api/Program.cs` (`SetupConfiguration`)

**Interfaces:**
- Produces: `internal static void Program.SetupConfiguration(ConfigurationManager configuration, IServiceCollection services)` (visibility widened from private; `InternalsVisibleTo("Test")` already exists).

- [ ] **Step 1: Write the failing tests**

`test/Test/Configuration/ConfigurationPrecedenceTests.cs`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;

namespace Test.Configuration
{
    /// <summary>
    /// CR-13: SetupConfiguration used to re-add appsettings.json after every default source, so it
    /// overrode user secrets, appsettings.{Env}.json, plain env vars and the command line.
    /// </summary>
    public class ConfigurationPrecedenceTests : IDisposable
    {
        private readonly string _contentRoot;
        private readonly List<WebApplicationBuilder> _builders = new();

        public ConfigurationPrecedenceTests()
        {
            _contentRoot = Path.Combine(Path.GetTempPath(), "awtrixsharp-ws7-config-" + Guid.NewGuid());
            Directory.CreateDirectory(_contentRoot);
            File.WriteAllText(Path.Combine(_contentRoot, "appsettings.json"), """
                {
                  "Awtrix": { "Devices": [ { "BaseTopic": "from-appsettings" } ] },
                  "Ws7Test": { "Value": "from-appsettings", "EnvFile": "from-appsettings", "Prefixed": "from-appsettings" }
                }
                """);
            File.WriteAllText(Path.Combine(_contentRoot, "appsettings.Development.json"), """
                { "Ws7Test": { "EnvFile": "from-development-json" } }
                """);
        }

        private WebApplicationBuilder CreateBuilder(string environment, params string[] args)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = args,
                ContentRootPath = _contentRoot,
                EnvironmentName = environment,
            });
            AwtrixSharpWeb.Program.SetupConfiguration(builder.Configuration, builder.Services);
            _builders.Add(builder);
            return builder;
        }

        [Fact]
        public void AppsettingsJson_IsStillLoaded()
        {
            var builder = CreateBuilder("Production");

            Assert.Equal("from-appsettings", builder.Configuration["Ws7Test:Value"]);
        }

        [Fact]
        public void CommandLine_OverridesAppsettingsJson()
        {
            var builder = CreateBuilder("Production", "--Awtrix:Devices:0:BaseTopic=from-command-line");

            Assert.Equal("from-command-line", builder.Configuration["Awtrix:Devices:0:BaseTopic"]);
        }

        [Fact]
        public void EnvironmentSpecificJson_OverridesAppsettingsJson()
        {
            var builder = CreateBuilder("Development");

            Assert.Equal("from-development-json", builder.Configuration["Ws7Test:EnvFile"]);
        }

        [Fact]
        public void AwtrixSharpPrefixedEnvironmentVariable_StillWinsOverCommandLine()
        {
            const string name = "AWTRIXSHARP_WS7TEST__PREFIXED";
            var previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, "from-prefixed-env");
            try
            {
                var builder = CreateBuilder("Production", "--Ws7Test:Prefixed=from-command-line");

                Assert.Equal("from-prefixed-env", builder.Configuration["Ws7Test:Prefixed"]);
            }
            finally
            {
                Environment.SetEnvironmentVariable(name, previous);
            }
        }

        [Fact]
        public void AppsettingsJson_IsRegisteredExactlyOnce()
        {
            var builder = CreateBuilder("Production");

            var count = ((IConfigurationBuilder)builder.Configuration).Sources
                .OfType<JsonConfigurationSource>()
                .Count(s => string.Equals(s.Path, "appsettings.json", StringComparison.OrdinalIgnoreCase));

            Assert.Equal(1, count);
        }

        public void Dispose()
        {
            foreach (var builder in _builders)
            {
                (builder.Configuration as IDisposable)?.Dispose();
            }

            try
            {
                Directory.Delete(_contentRoot, recursive: true);
            }
            catch (IOException)
            {
                // A file watcher may still hold the directory on Windows; the temp folder is disposable.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Configuration.ConfigurationPrecedenceTests"`
Expected: build FAILS with `CS0122: 'Program.SetupConfiguration(ConfigurationManager, IServiceCollection)' is inaccessible due to its protection level`.

- [ ] **Step 3: Make `SetupConfiguration` internal and remove the duplicate source**

In `src/api/Program.cs`, replace the whole `SetupConfiguration` method with:

```csharp
        /// <summary>
        /// WebApplication.CreateBuilder already loads appsettings.json, appsettings.{Env}.json, user secrets,
        /// environment variables and the command line (in that order). Only the AWTRIXSHARP_ provider is added
        /// here, last, so it keeps overriding everything (CR-13: appsettings.json is not re-added).
        /// </summary>
        internal static void SetupConfiguration(ConfigurationManager configuration, IServiceCollection services)
        {
            configuration.AddEnvironmentVariables("AWTRIXSHARP_");

            services.Configure<MqttSettings>(configuration.GetSection("Mqtt"));
            services.Configure<AwtrixConfig>(configuration.GetSection("Awtrix"));
        }
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Configuration.ConfigurationPrecedenceTests"`
Expected: PASS, 5 tests, 0 failed. Before Step 3, `CommandLine_...`, `EnvironmentSpecificJson_...` and `..._IsRegisteredExactlyOnce` would have failed.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test`
Expected: 0 failed.

- [ ] **Step 6: Commit**

```bash
git add src/api/Program.cs test/Test/Configuration/ConfigurationPrecedenceTests.cs
git commit -m "fix(config): load appsettings.json once so standard precedence applies

CR-13: SetupConfiguration re-added appsettings.json after user secrets,
appsettings.{Env}.json, env vars and the command line, silently
overriding them. Only the AWTRIXSHARP_ provider is added now, and it
still wins over everything. Release note in the WS7 spec section 7.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

### Task 2: Secrets and settings through `IConfiguration`, with env-var fallback and startup warnings (CR-14)

**Files:**
- Create: `src/api/Domain/SlackSettings.cs`, `src/api/Domain/DataSettings.cs`, `src/api/Domain/ConfigurationWarnings.cs`
- Create: `test/Test/Configuration/SettingsResolutionTests.cs`, `test/Test/Configuration/ConfigurationWarningsTests.cs`, `test/Test/HostedServices/ConductorSlackSettingsTests.cs`
- Modify: `src/api/Program.cs` (`AddAwtrixServices`, new `AddSettings`, `LogStartup`)
- Modify: `src/api/HostedServices/SlackConnector.cs` (constructor, `ExecuteAsync` token read)
- Modify: `src/api/HostedServices/Conductor.cs` (constructor, `SlackStatusApp` factory branch)
- Modify: `src/api/Services/TripPlanner/TripPlannerService.cs` (constructor, `TryLocalCache`)
- Modify: `test/Test/HostedServices/ConductorTestHelper.cs`, `test/Test/TripPlanner/TripPlannerServiceTests.cs`

**Interfaces:**
- Consumes: `Program.AddAwtrixServices(IServiceCollection, IConfiguration)`; `ConductorTestHelper.Create(...)`; `Conductor.FindApps(string, string? = null)`; `IAwtrixApp.GetConfig() : IAppConfig`.
- Produces:
  - `public class SlackSettings { const string SectionName = "Slack"; const string AppTokenEnvironmentVariable; const string UserIdEnvironmentVariable; string? AppToken; string? UserId; SlackSettings WithEnvironmentFallback(); }`
  - `public class DataSettings { const string DataDirectoryKey = "Settings:DATA_DIRECTORY"; const string DataDirectoryEnvironmentVariable; string? DataDirectory; DataSettings WithEnvironmentFallback(); }`
  - `internal static class ConfigurationWarnings { static IReadOnlyList<string> Get(AwtrixConfig, TransportOpenDataConfig, SlackSettings); }`
  - `public SlackConnector(ILogger<SlackConnector> logger, IOptions<SlackSettings>? slackSettings = null)`, `internal string? ResolveAppToken()`
  - `Conductor` constructor gains a last parameter `IOptions<SlackSettings>? slackSettings = null`
  - `TripPlannerService` constructor gains a last parameter `IOptions<DataSettings>? dataSettings = null`
  - `internal const string Program.DefaultTransportOpenDataBaseUrl = "https://api.transport.nsw.gov.au/v1/tp"`
  - `ConductorTestHelper.Create(..., SlackSettings? slackSettings = null)`

- [ ] **Step 1: Write the failing settings tests**

`test/Test/Configuration/SettingsResolutionTests.cs`:

```csharp
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TransportOpenData;

namespace Test.Configuration
{
    /// <summary>
    /// CR-14: secrets and settings come from IConfiguration (appsettings, user secrets, AWTRIXSHARP_ provider),
    /// falling back to the literal environment variable names.
    /// </summary>
    public class SettingsResolutionTests
    {
        private static ServiceProvider Build(Dictionary<string, string?>? settings = null)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOptions();
            services.AddSingleton<IHostEnvironment>(Mock.Of<IHostEnvironment>(e => e.EnvironmentName == "Production"));

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(settings ?? new Dictionary<string, string?>())
                .Build();

            AwtrixSharpWeb.Program.AddAwtrixServices(services, configuration);
            return services.BuildServiceProvider();
        }

        [Fact]
        public void TransportOpenDataApiKey_IsReadFromConfiguration()
        {
            using var provider = Build(new() { ["TransportOpenData:ApiKey"] = "key-from-config" });

            Assert.Equal("key-from-config", provider.GetRequiredService<IOptions<TransportOpenDataConfig>>().Value.ApiKey);
        }

        [Fact]
        public void TransportOpenDataApiKey_FallsBackToLiteralEnvironmentVariable()
        {
            using var provider = Build();

            var expected = Environment.GetEnvironmentVariable("TRANSPORTOPENDATA__APIKEY") ?? string.Empty;
            Assert.Equal(expected, provider.GetRequiredService<IOptions<TransportOpenDataConfig>>().Value.ApiKey);
        }

        [Fact]
        public void TransportOpenDataBaseUrl_DefaultsToTripPlannerUrl_AndCanBeOverridden()
        {
            using (var provider = Build())
            {
                Assert.Equal("https://api.transport.nsw.gov.au/v1/tp",
                    provider.GetRequiredService<IOptions<TransportOpenDataConfig>>().Value.BaseUrl);
            }

            using (var provider = Build(new() { ["TransportOpenData:BaseUrl"] = "https://example.test/tp" }))
            {
                Assert.Equal("https://example.test/tp",
                    provider.GetRequiredService<IOptions<TransportOpenDataConfig>>().Value.BaseUrl);
            }
        }

        [Fact]
        public void SlackSettings_AreReadFromConfiguration()
        {
            using var provider = Build(new()
            {
                ["Slack:AppToken"] = "xapp-from-config",
                ["Slack:UserId"] = "U-FROM-CONFIG",
            });

            var slack = provider.GetRequiredService<IOptions<SlackSettings>>().Value;
            Assert.Equal("xapp-from-config", slack.AppToken);
            Assert.Equal("U-FROM-CONFIG", slack.UserId);
        }

        [Fact]
        public void DataSettings_AreReadFromSettingsDataDirectoryKey()
        {
            using var provider = Build(new() { ["Settings:DATA_DIRECTORY"] = "/data/awtrix" });

            Assert.Equal("/data/awtrix", provider.GetRequiredService<IOptions<DataSettings>>().Value.DataDirectory);
        }

        [Fact]
        public void SlackConnector_ResolveAppToken_UsesOptions()
        {
            var connector = new SlackConnector(
                NullLogger<SlackConnector>.Instance,
                Options.Create(new SlackSettings { AppToken = "xapp-from-options" }));

            Assert.Equal("xapp-from-options", connector.ResolveAppToken());
        }

        [Fact]
        public void SlackConnector_ResolveAppToken_WithoutOptions_UsesLiteralEnvironmentVariable()
        {
            var connector = new SlackConnector(NullLogger<SlackConnector>.Instance);

            Assert.Equal(Environment.GetEnvironmentVariable("AWTRIXSHARP_SLACK__APPTOKEN"), connector.ResolveAppToken());
        }
    }
}
```

`test/Test/Configuration/ConfigurationWarningsTests.cs`:

```csharp
using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using TransportOpenData;

namespace Test.Configuration
{
    public class ConfigurationWarningsTests
    {
        private static AwtrixConfig ConfigWith(params string[] appTypes) => new()
        {
            Devices = new[]
            {
                new DeviceConfig
                {
                    BaseTopic = "awtrix/clock1",
                    Apps = appTypes.Select(t => new AppConfig { Type = t }).ToList(),
                },
            },
        };

        [Fact]
        public void TripTimerApp_WithoutApiKey_Warns()
        {
            var warnings = ConfigurationWarnings.Get(ConfigWith("TripTimerApp"), new TransportOpenDataConfig { ApiKey = "" }, new SlackSettings());

            var warning = Assert.Single(warnings);
            Assert.Contains("TRANSPORTOPENDATA__APIKEY", warning);
        }

        [Fact]
        public void SlackStatusApp_WithoutAppToken_Warns()
        {
            var warnings = ConfigurationWarnings.Get(ConfigWith("SlackStatusApp"), new TransportOpenDataConfig(), new SlackSettings { AppToken = " " });

            var warning = Assert.Single(warnings);
            Assert.Contains("AWTRIXSHARP_SLACK__APPTOKEN", warning);
        }

        [Fact]
        public void ConfiguredSecrets_NoWarnings()
        {
            var warnings = ConfigurationWarnings.Get(
                ConfigWith("TripTimerApp", "SlackStatusApp"),
                new TransportOpenDataConfig { ApiKey = "key" },
                new SlackSettings { AppToken = "xapp-1" });

            Assert.Empty(warnings);
        }

        [Fact]
        public void AppsNotConfigured_NoWarnings_EvenWithoutSecrets()
        {
            var warnings = ConfigurationWarnings.Get(ConfigWith("DiurnalApp"), new TransportOpenDataConfig(), new SlackSettings());

            Assert.Empty(warnings);
        }
    }
}
```

`test/Test/HostedServices/ConductorSlackSettingsTests.cs`:

```csharp
using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;

namespace Test.HostedServices
{
    /// <summary>
    /// CR-14: a blank SlackStatusApp SlackUserId is filled from Slack:UserId (which is where
    /// AWTRIXSHARP_SLACK__USERID arrives through the prefixed provider). An explicit value still wins.
    /// </summary>
    public class ConductorSlackSettingsTests
    {
        private static AwtrixConfig ConfigWithSlackUserId(string slackUserId)
        {
            var app = new AppConfig { Type = AppNames.SlackStatusApp };
            app.Config.Add("SlackUserId", slackUserId);
            return new AwtrixConfig
            {
                Devices = new[] { new DeviceConfig { BaseTopic = "awtrix/clock1", Apps = new List<AppConfig> { app } } },
            };
        }

        [Fact]
        public async Task BlankSlackUserId_IsFilledFromSlackSettings()
        {
            var conductor = ConductorTestHelper.Create(
                ConfigWithSlackUserId(""),
                slackSettings: new SlackSettings { UserId = "U-FROM-CONFIG" });

            await conductor.StartAsync(CancellationToken.None);
            try
            {
                var app = Assert.Single(conductor.FindApps(AppNames.SlackStatusApp));
                Assert.Equal("U-FROM-CONFIG", ((AppConfig)app.GetConfig()).Config.Get("SlackUserId"));
            }
            finally
            {
                await conductor.StopAsync(CancellationToken.None);
            }
        }

        [Fact]
        public async Task ExplicitSlackUserId_IsKept()
        {
            var conductor = ConductorTestHelper.Create(
                ConfigWithSlackUserId("U-EXPLICIT"),
                slackSettings: new SlackSettings { UserId = "U-FROM-CONFIG" });

            await conductor.StartAsync(CancellationToken.None);
            try
            {
                var app = Assert.Single(conductor.FindApps(AppNames.SlackStatusApp));
                Assert.Equal("U-EXPLICIT", ((AppConfig)app.GetConfig()).Config.Get("SlackUserId"));
            }
            finally
            {
                await conductor.StopAsync(CancellationToken.None);
            }
        }
    }
}
```

Append this test inside `TripPlannerServiceTests` (after `GetNextDepartures_NoEnvVarSet_FallsBackToTripClient`), and add `using AwtrixSharpWeb.Domain;` and `using Microsoft.Extensions.Options;` at the top of `test/Test/TripPlanner/TripPlannerServiceTests.cs`. If Task 0 Step 5 recorded `GetNextDepartures(..., DateTimeOffset)`, pass `new DateTimeOffset(fromWhen)`.

```csharp
        [Fact]
        public async Task GetNextDepartures_UsesFileCacheDirectoryFromDataSettings_WhenEnvironmentVariableUnset()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), "awtrixsharp-tests-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            var envVarName = "AWTRIXSHARP_SETTINGS__DATA_DIRECTORY";
            var previousValue = Environment.GetEnvironmentVariable(envVarName);

            try
            {
                Environment.SetEnvironmentVariable(envVarName, null);
                var fromWhen = new DateTime(2025, 1, 1, 8, 0, 0);
                var offset = DateTimeOffset.Now.Offset;
                var cachedTrips = new List<TripSummary>
                {
                    new TripSummary
                    {
                        Origin = new TimePlace { Time = new DateTimeOffset(2000, 1, 1, 6, 30, 0, offset), Place = "ConfiguredCache" },
                        Destination = new TimePlace { Time = new DateTimeOffset(2000, 1, 1, 7, 0, 0, offset), Place = "Destination" }
                    }
                };
                File.WriteAllText(Path.Combine(tempDir, $"trip_originA_destB_{fromWhen:HH}.json"), JsonSerializer.Serialize(cachedTrips));

                var sut = new TripPlannerService(
                    _mockStopFinderClient.Object,
                    _mockTripClient.Object,
                    _mockLogger.Object,
                    Options.Create(new DataSettings { DataDirectory = tempDir }));

                // Act
                var result = await sut.GetNextDepartures("originA", "destB", fromWhen);

                // Assert
                var summary = Assert.Single(result);
                Assert.Equal("ConfiguredCache", summary.Origin.Place);
            }
            finally
            {
                Environment.SetEnvironmentVariable(envVarName, previousValue);
                Directory.Delete(tempDir, true);
            }
        }
```

In `test/Test/HostedServices/ConductorTestHelper.cs`:
- add `using AwtrixSharpWeb.Domain;` if missing
- append the parameter `SlackSettings? slackSettings = null` as the **last** parameter of `Create`
- pass `slackSettings: slackSettings is null ? null : Options.Create(slackSettings)` as the last argument of `new Conductor(...)`

For the current WS1 shape that looks like this (keep any extra WS3 parameters in place):

```csharp
        public static Conductor Create(
            AwtrixConfig? config = null,
            IHostEnvironment? hostEnvironment = null,
            IAwtrixService? awtrixService = null,
            IMqttConnector? mqttConnector = null,
            IClock? clock = null,
            SlackSettings? slackSettings = null)
        {
            // ... unchanged body up to the constructor call ...
            return new Conductor(
                NullLogger<Conductor>.Instance,
                env,
                Options.Create(config),
                new Mock<ITimerService>().Object,
                new Mock<ITripPlannerService>().Object,
                awtrixService ?? new Mock<IAwtrixService>().Object,
                new Mock<ISlackConnector>().Object,
                mqttConnector ?? new Mock<IMqttConnector>().Object,
                clock ?? new Clock(),
                NullLoggerFactory.Instance,
                slackSettings: slackSettings is null ? null : Options.Create(slackSettings));
        }
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Configuration.SettingsResolutionTests|FullyQualifiedName~Test.Configuration.ConfigurationWarningsTests|FullyQualifiedName~Test.HostedServices.ConductorSlackSettingsTests|FullyQualifiedName~TripPlannerServiceTests"`
Expected: build FAILS with `CS0246: The type or namespace name 'SlackSettings' could not be found` (also `DataSettings`, `ConfigurationWarnings`).

- [ ] **Step 3: Implement settings classes and registration**

`src/api/Domain/SlackSettings.cs`:

```csharp
namespace AwtrixSharpWeb.Domain
{
    /// <summary>
    /// The "Slack" configuration section. AWTRIXSHARP_SLACK__APPTOKEN / AWTRIXSHARP_SLACK__USERID arrive here
    /// through the AWTRIXSHARP_ provider; user secrets and appsettings work too. The literal environment
    /// variables stay as a final fallback (CR-14).
    /// </summary>
    public class SlackSettings
    {
        public const string SectionName = "Slack";
        public const string AppTokenEnvironmentVariable = "AWTRIXSHARP_SLACK__APPTOKEN";
        public const string UserIdEnvironmentVariable = "AWTRIXSHARP_SLACK__USERID";

        /// <summary>Slack app-level token (xapp-...)</summary>
        public string? AppToken { get; set; }

        /// <summary>Default Slack user id for SlackStatusApp when the app config leaves SlackUserId blank</summary>
        public string? UserId { get; set; }

        public SlackSettings WithEnvironmentFallback()
        {
            if (string.IsNullOrWhiteSpace(AppToken))
            {
                AppToken = Environment.GetEnvironmentVariable(AppTokenEnvironmentVariable);
            }

            if (string.IsNullOrWhiteSpace(UserId))
            {
                UserId = Environment.GetEnvironmentVariable(UserIdEnvironmentVariable);
            }

            return this;
        }
    }
}
```

`src/api/Domain/DataSettings.cs`:

```csharp
namespace AwtrixSharpWeb.Domain
{
    /// <summary>
    /// Local data settings. The historical key is Settings:DATA_DIRECTORY (env AWTRIXSHARP_SETTINGS__DATA_DIRECTORY),
    /// which is not a valid property name, so it is read explicitly rather than bound (CR-14).
    /// </summary>
    public class DataSettings
    {
        public const string DataDirectoryKey = "Settings:DATA_DIRECTORY";
        public const string DataDirectoryEnvironmentVariable = "AWTRIXSHARP_SETTINGS__DATA_DIRECTORY";

        /// <summary>Folder holding optional trip cache files; blank disables the cache</summary>
        public string? DataDirectory { get; set; }

        public DataSettings WithEnvironmentFallback()
        {
            if (string.IsNullOrWhiteSpace(DataDirectory))
            {
                DataDirectory = Environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariable);
            }

            return this;
        }
    }
}
```

`src/api/Domain/ConfigurationWarnings.cs`:

```csharp
using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.HostedServices;
using TransportOpenData;

namespace AwtrixSharpWeb.Domain
{
    /// <summary>
    /// Startup checks for missing secrets that would otherwise only surface hours later (CR-14). Never throws.
    /// </summary>
    internal static class ConfigurationWarnings
    {
        public static IReadOnlyList<string> Get(AwtrixConfig awtrix, TransportOpenDataConfig transportOpenData, SlackSettings slack)
        {
            var configuredTypes = (awtrix.Devices ?? Array.Empty<DeviceConfig>())
                .SelectMany(d => d.Apps ?? new List<AppConfig>())
                .Select(a => a?.Type)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToHashSet(StringComparer.Ordinal);

            var warnings = new List<string>();

            if (configuredTypes.Contains(AppNames.TripTimerApp) && string.IsNullOrWhiteSpace(transportOpenData.ApiKey))
            {
                warnings.Add("TripTimerApp is configured but no Transport NSW API key was found " +
                             "(TransportOpenData:ApiKey or TRANSPORTOPENDATA__APIKEY); departure lookups will be rejected");
            }

            if (configuredTypes.Contains(AppNames.SlackStatusApp) && string.IsNullOrWhiteSpace(slack.AppToken))
            {
                warnings.Add("SlackStatusApp is configured but no Slack app token was found " +
                             "(Slack:AppToken or AWTRIXSHARP_SLACK__APPTOKEN); Slack status will not be shown");
            }

            return warnings;
        }
    }
}
```

In `src/api/Program.cs`:

1. Replace the block

```csharp
            // Configure Trip Planner settings
            services.Configure<TransportOpenDataConfig>(config =>
            {
                config.ApiKey = Environment.GetEnvironmentVariable("TRANSPORTOPENDATA__APIKEY") ?? "";
                config.BaseUrl = configuration.GetSection("TransportOpenData:BaseUrl").Value ?? "https://api.transport.nsw.gov.au/v1/tp";
            });
```

with

```csharp
            // Settings and secrets: IConfiguration first, literal environment variable as fallback (CR-14)
            AddSettings(services, configuration);
```

2. Add these members to `Program` (below `AddAwtrixServices`):

```csharp
        internal const string DefaultTransportOpenDataBaseUrl = "https://api.transport.nsw.gov.au/v1/tp";
        internal const string TransportOpenDataApiKeyEnvironmentVariable = "TRANSPORTOPENDATA__APIKEY";

        private static void AddSettings(IServiceCollection services, IConfiguration configuration)
        {
            // Explicit reads rather than Bind: TransportOpenDataConfig.BaseUrl defaults to .../v1 in the class,
            // while this service has always used .../v1/tp.
            services.AddOptions<TransportOpenDataConfig>().Configure(config =>
            {
                config.ApiKey = FirstNonBlank(
                    configuration["TransportOpenData:ApiKey"],
                    Environment.GetEnvironmentVariable(TransportOpenDataApiKeyEnvironmentVariable)) ?? string.Empty;
                config.BaseUrl = FirstNonBlank(configuration["TransportOpenData:BaseUrl"]) ?? DefaultTransportOpenDataBaseUrl;
            });

            services.AddOptions<SlackSettings>()
                .Bind(configuration.GetSection(SlackSettings.SectionName))
                .PostConfigure(settings => settings.WithEnvironmentFallback());

            services.AddOptions<DataSettings>()
                .Configure(settings => settings.DataDirectory = configuration[DataSettings.DataDirectoryKey])
                .PostConfigure(settings => settings.WithEnvironmentFallback());
        }

        internal static string? FirstNonBlank(params string?[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
```

3. At the end of `LogStartup`, add:

```csharp
            var warnings = ConfigurationWarnings.Get(
                app.Services.GetRequiredService<IOptions<AwtrixConfig>>().Value,
                app.Services.GetRequiredService<IOptions<TransportOpenDataConfig>>().Value,
                app.Services.GetRequiredService<IOptions<SlackSettings>>().Value);

            foreach (var warning in warnings)
            {
                logger.LogWarning("Configuration: {Warning}", warning);
            }
```

- [ ] **Step 4: Wire the consumers**

`src/api/HostedServices/SlackConnector.cs`:
- add `using AwtrixSharpWeb.Domain;` and `using Microsoft.Extensions.Options;`
- replace the constructor with:

```csharp
        private readonly IOptions<SlackSettings>? _slackSettings;

        public SlackConnector(ILogger<SlackConnector> logger, IOptions<SlackSettings>? slackSettings = null)
        {
            _logger = logger;
            _slackSettings = slackSettings;
        }

        /// <summary>
        /// Slack:AppToken from configuration; without options (hand-built instances) only the literal
        /// AWTRIXSHARP_SLACK__APPTOKEN environment variable, as before (CR-14).
        /// </summary>
        internal string? ResolveAppToken() =>
            (_slackSettings?.Value ?? new SlackSettings().WithEnvironmentFallback()).AppToken;
```

- in `ExecuteAsync`, replace

```csharp
                var appToken = Environment.GetEnvironmentVariable("AWTRIXSHARP_SLACK__APPTOKEN"); // xapp-***

                if (string.IsNullOrEmpty(appToken))
                {
                    _logger.LogWarning("Slack AppToken not configured. Slack integration disabled.");
                    return;
                }
```

with

```csharp
                var appToken = ResolveAppToken(); // xapp-***

                if (string.IsNullOrWhiteSpace(appToken))
                {
                    _logger.LogWarning("Slack AppToken not configured (Slack:AppToken or AWTRIXSHARP_SLACK__APPTOKEN). Slack integration disabled.");
                    return;
                }
```

`src/api/HostedServices/Conductor.cs`:
- add `using AwtrixSharpWeb.Domain;` if missing
- add the field `private readonly SlackSettings? _slackSettings;`
- append `, IOptions<SlackSettings>? slackSettings = null` as the **last** constructor parameter, and in the body `_slackSettings = slackSettings?.Value;`
- in `AppFactory`, replace the `SlackStatusApp` branch body with:

```csharp
                case AppNames.SlackStatusApp:
                    {
                        var appLogger = _loggerFactory.CreateLogger<SlackStatusApp>();
                        var slackStatusConfig = appConfig.As<SlackStatusAppConfig>();

                        // CR-14: blank app-level SlackUserId falls back to Slack:UserId (AWTRIXSHARP_SLACK__USERID).
                        // As<T>() cloned the keys, so the bound AwtrixConfig is not mutated.
                        if (string.IsNullOrWhiteSpace(slackStatusConfig.Config.Get("SlackUserId"))
                            && !string.IsNullOrWhiteSpace(_slackSettings?.UserId))
                        {
                            slackStatusConfig.SetConfig("SlackUserId", _slackSettings.UserId);
                        }

                        app = new SlackStatusApp(appLogger, slackStatusConfig, device, _awtrixService, _slackConnector);
                    }
                    break;
```

(If WS3 changed the branch to `return new SlackStatusApp(...)`, keep that return style; insert only the `if` block after `As<SlackStatusAppConfig>()`.)

`src/api/Services/TripPlanner/TripPlannerService.cs`:
- add `using AwtrixSharpWeb.Domain;` and `using Microsoft.Extensions.Options;`
- add the field `private readonly IOptions<DataSettings>? _dataSettings;`
- append `, IOptions<DataSettings>? dataSettings = null` as the **last** constructor parameter, and in the body `_dataSettings = dataSettings;`
- in `TryLocalCache`, replace

```csharp
            var cacheFolder = Environment.GetEnvironmentVariable("AWTRIXSHARP_SETTINGS__DATA_DIRECTORY");
```

with

```csharp
            // CR-14: Settings:DATA_DIRECTORY from configuration; hand-built instances read only the env var, as before
            var cacheFolder = (_dataSettings?.Value ?? new DataSettings().WithEnvironmentFallback()).DataDirectory;
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Configuration|FullyQualifiedName~Test.HostedServices.ConductorSlackSettingsTests|FullyQualifiedName~TripPlannerServiceTests|FullyQualifiedName~Test.CompositionRootTests|FullyQualifiedName~SlackStatusAppTests"`
Expected: PASS, 0 failed. `CompositionRootTests` confirms the graph still validates with the optional parameters.

- [ ] **Step 6: Run the full suite and confirm the env-var reads are gone from runtime code**

Run: `dotnet test`
Expected: 0 failed.

Run: `grep -rn "GetEnvironmentVariable" src/api --include=*.cs`
Expected: only `Domain/SlackSettings.cs`, `Domain/DataSettings.cs`, `Program.cs` (the TfNSW fallback), `Apps/Configs/AppConfigKeys.cs` (the `Get(key, envVar)` fallback) and `Debug/SlackUserHarvester.cs` (out of scope, WS8).

- [ ] **Step 7: Commit**

```bash
git add src/api/Domain/SlackSettings.cs src/api/Domain/DataSettings.cs src/api/Domain/ConfigurationWarnings.cs src/api/Program.cs src/api/HostedServices/SlackConnector.cs src/api/HostedServices/Conductor.cs src/api/Services/TripPlanner/TripPlannerService.cs test/Test/Configuration/SettingsResolutionTests.cs test/Test/Configuration/ConfigurationWarningsTests.cs test/Test/HostedServices/ConductorSlackSettingsTests.cs test/Test/HostedServices/ConductorTestHelper.cs test/Test/TripPlanner/TripPlannerServiceTests.cs
git commit -m "fix(config): read secrets and settings through IConfiguration

CR-14: TransportOpenData:ApiKey, Slack:AppToken, Slack:UserId and
Settings:DATA_DIRECTORY now come from configuration (appsettings, user
secrets, AWTRIXSHARP_ provider), with the literal env var names kept as
a final fallback. TransportOpenData BaseUrl default stays .../v1/tp.
Startup warns when TripTimerApp has no TfNSW key or SlackStatusApp has
no Slack token.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

### Task 3: Case-insensitive app config and ValueMap keys (CR-22)

**Files:**
- Create: `test/Test/Configs/ConfigKeyCaseTests.cs`
- Modify: `src/api/Apps/Configs/AppConfigKeys.cs` (constructor), `src/api/Apps/Configs/ValueMap.cs` (add constructor)

**Interfaces:**
- Produces: `AppConfigKeys()` and `ValueMap()` use `StringComparer.OrdinalIgnoreCase`; `Clone()` keeps it.

- [ ] **Step 1: Write the failing tests**

`test/Test/Configs/ConfigKeyCaseTests.cs`:

```csharp
using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Apps.TripTimer;
using AwtrixSharpWeb.Domain;
using Microsoft.Extensions.Configuration;

namespace Test.Configs
{
    /// <summary>
    /// CR-22: IConfiguration keys are case-insensitive and the env-var provider keeps the variable's casing
    /// (AWTRIXSHARP_..._CONFIG__STOPIDORIGIN), so the app config dictionaries must ignore case too.
    /// </summary>
    public class ConfigKeyCaseTests
    {
        private static AppConfig BindFirstApp(params Dictionary<string, string?>[] layers)
        {
            var builder = new ConfigurationBuilder();
            foreach (var layer in layers)
            {
                builder.AddInMemoryCollection(layer);
            }

            var awtrix = builder.Build().GetSection("Awtrix").Get<AwtrixConfig>();
            Assert.NotNull(awtrix);
            return awtrix!.Devices[0].Apps[0];
        }

        [Fact]
        public void AppConfigKeys_Get_IgnoresCase()
        {
            var sut = new AppConfigKeys { { "StopIdOrigin", "200060" } };

            Assert.Equal("200060", sut.Get("STOPIDORIGIN"));
        }

        [Fact]
        public void AppConfigKeys_Clone_KeepsCaseInsensitivity()
        {
            var clone = new AppConfigKeys { { "StopIdOrigin", "200060" } }.Clone();

            Assert.Equal("200060", clone.Get("stopidorigin"));
        }

        [Fact]
        public void ValueMap_LowercaseValueMatcherKey_IsUsed()
        {
            var sut = new ValueMap { { "valueMatcher", "^-" } };

            Assert.Equal("^-", sut.ValueMatcher);
            Assert.True(sut.IsMatch("-12"));
        }

        [Fact]
        public void ValueMap_Clone_KeepsCaseInsensitivity()
        {
            var clone = new ValueMap { { "valueMatcher", "busy" } }.Clone();

            Assert.Equal("busy", clone.ValueMatcher);
        }

        [Fact]
        public void Binding_EnvVarStyleUppercaseKey_ReachesTypedProperty()
        {
            var app = BindFirstApp(new Dictionary<string, string?>
            {
                ["Awtrix:Devices:0:BaseTopic"] = "awtrix/clock1",
                ["Awtrix:Devices:0:Apps:0:Type"] = "TripTimerApp",
                ["Awtrix:Devices:0:Apps:0:Config:STOPIDORIGIN"] = "200060",
            });

            Assert.Equal("200060", app.As<TripTimerAppConfig>().StopIdOrigin);
        }

        [Fact]
        public void Binding_CamelCaseCronSchedule_ReachesTypedProperty()
        {
            var app = BindFirstApp(new Dictionary<string, string?>
            {
                ["Awtrix:Devices:0:BaseTopic"] = "awtrix/clock1",
                ["Awtrix:Devices:0:Apps:0:Type"] = "MqttRenderApp",
                ["Awtrix:Devices:0:Apps:0:Config:cronSchedule"] = "0 8 * * *",
            });

            Assert.Equal("0 8 * * *", app.As<ScheduledAppConfig>().CronSchedule);
        }

        [Fact]
        public void Binding_LaterUppercaseOverride_Wins()
        {
            var app = BindFirstApp(
                new Dictionary<string, string?>
                {
                    ["Awtrix:Devices:0:BaseTopic"] = "awtrix/clock1",
                    ["Awtrix:Devices:0:Apps:0:Type"] = "TripTimerApp",
                    ["Awtrix:Devices:0:Apps:0:Config:StopIdOrigin"] = "200060",
                },
                new Dictionary<string, string?>
                {
                    ["Awtrix:Devices:0:Apps:0:Config:STOPIDORIGIN"] = "999999",
                });

            Assert.Equal("999999", app.As<TripTimerAppConfig>().StopIdOrigin);
        }

        [Fact]
        public void Binding_LowercaseValueMatcher_Matches()
        {
            var app = BindFirstApp(new Dictionary<string, string?>
            {
                ["Awtrix:Devices:0:BaseTopic"] = "awtrix/clock1",
                ["Awtrix:Devices:0:Apps:0:Type"] = "MqttRenderApp",
                ["Awtrix:Devices:0:Apps:0:ValueMaps:0:valueMatcher"] = "^-",
                ["Awtrix:Devices:0:Apps:0:ValueMaps:0:Icon"] = "52465",
            });

            Assert.NotNull(app.FindMatchingValueMap("-3.2"));
        }
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Configs.ConfigKeyCaseTests"`
Expected: FAIL. `AppConfigKeys_Get_IgnoresCase`, `AppConfigKeys_Clone_KeepsCaseInsensitivity`, both `ValueMap_*`, `Binding_EnvVarStyleUppercaseKey_...`, `Binding_CamelCaseCronSchedule_...` and `Binding_LowercaseValueMatcher_Matches` fail. `Binding_LaterUppercaseOverride_Wins` may pass already; it is a regression guard.

- [ ] **Step 3: Implement the comparer**

In `src/api/Apps/Configs/AppConfigKeys.cs`, replace

```csharp
        public AppConfigKeys()
        {
                
        }
```

with

```csharp
        /// <summary>
        /// Keys ignore case, matching IConfiguration (CR-22). Clone() uses this constructor, so clones do too.
        /// </summary>
        public AppConfigKeys() : base(StringComparer.OrdinalIgnoreCase)
        {
        }
```

In `src/api/Apps/Configs/ValueMap.cs`, add directly after `public class ValueMap : Dictionary<string, string>` + `{`:

```csharp
        /// <summary>
        /// Keys ignore case, matching IConfiguration (CR-22). Clone() uses this constructor, so clones do too.
        /// </summary>
        public ValueMap() : base(StringComparer.OrdinalIgnoreCase)
        {
        }

```

(If WS5 already added a `ValueMap` constructor, add `: base(StringComparer.OrdinalIgnoreCase)` to it instead.)

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Configs"`
Expected: PASS, 0 failed.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test`
Expected: 0 failed.

- [ ] **Step 6: Commit**

```bash
git add src/api/Apps/Configs/AppConfigKeys.cs src/api/Apps/Configs/ValueMap.cs test/Test/Configs/ConfigKeyCaseTests.cs
git commit -m "fix(config): case-insensitive app config and ValueMap keys

CR-22: AppConfigKeys and ValueMap used the ordinal comparer, so
env-var overrides such as ..._CONFIG__STOPIDORIGIN, \"cronSchedule\" and
\"valueMatcher\" were ignored. Both now use OrdinalIgnoreCase, and
Clone() keeps it. Correctly cased keys behave as before.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

### Task 4: Invariant-culture typed config, `Validate()`, and startup rejection inside the per-app guard (CR-23)

**Files:**
- Create: `src/api/Apps/Configs/AppConfigValidationException.cs`
- Create: `test/Test/Configs/AppConfigValidationTests.cs`, `test/Test/HostedServices/ConductorConfigValidationTests.cs`
- Modify (full rewrite): `src/api/Apps/Configs/AppConfig.cs`, `src/api/Apps/Configs/ScheduledAppConfig.cs`, `src/api/Apps/MqttRender/MqttAppConfig.cs`, `src/api/Apps/TripTimer/TripTimerAppConfig.cs`
- Modify: `src/api/HostedServices/Conductor.cs` (`AppFactory`: three `EnsureValid` calls)
- Modify: `test/Test/Configs/AppConfigConvertValueTests.cs` (replace the NRE test, add culture tests)
- Modify: `test/Test/HostedServices/ConductorStartupTests.cs` (the WS3 test recorded in Task 0 Step 3)

**Interfaces:**
- Consumes: WS3's per-app creation guard around `AppFactory`; `ConductorTestHelper.Create(config, awtrixService: ...)`; `IAwtrixService.AppClear(AwtrixAddress, string) : Task<bool>`.
- Produces:
  - `public virtual IReadOnlyList<string> AppConfig.Validate()`
  - `public void AppConfig.EnsureValid(string? device)`
  - `protected void AppConfig.ValidateRequired(List<string> errors, string key)`
  - `protected void AppConfig.ValidateTimeSpan(List<string> errors, string key, bool required, bool mustBePositive)`
  - `public class AppConfigValidationException : Exception { string? AppType; string? Device; IReadOnlyList<string> Errors; }`
  - `GetConfig<T>` returns `default(T)` for a missing or whitespace value.

- [ ] **Step 1: Write the failing validation tests**

`test/Test/Configs/AppConfigValidationTests.cs`:

```csharp
using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Apps.MqttRender;
using AwtrixSharpWeb.Apps.TripTimer;

namespace Test.Configs
{
    public class AppConfigValidationTests
    {
        private static T Build<T>(Dictionary<string, string> keys, string type) where T : AppConfig, new()
        {
            var source = AppConfig.Empty().WithName(type);
            foreach (var kvp in keys)
            {
                source.Config.Add(kvp.Key, kvp.Value);
            }
            return source.As<T>();
        }

        /// <summary>The shipped appsettings.json TripTimerApp config</summary>
        private static Dictionary<string, string> ShippedTripTimer() => new()
        {
            ["CronSchedule"] = "10 6 * * 1-5",
            ["ActiveTime"] = "01:00:00",
            ["StopIdOrigin"] = "200060",
            ["StopIdDestination"] = "200070",
            ["TimeToOrigin"] = "00:14:00",
            ["TimeToPrepare"] = "00:08:00",
        };

        private static Dictionary<string, string> ShippedMqttRender() => new()
        {
            ["CronSchedule"] = "0 8 * * *",
            ["ActiveTime"] = "09:00:00",
            ["ReadTopic"] = "openhab/fronius/grid-surplus",
        };

        [Fact]
        public void ShippedConfigs_AreValid()
        {
            Assert.Empty(Build<TripTimerAppConfig>(ShippedTripTimer(), "TripTimerApp").Validate());
            Assert.Empty(Build<MqttAppConfig>(ShippedMqttRender(), "MqttRenderApp").Validate());
        }

        [Fact]
        public void BaseAppConfig_HasNoRules()
        {
            Assert.Empty(new AppConfig().Validate());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("61 * * * *")]
        [InlineData("not a cron")]
        public void CronSchedule_MissingOrInvalid_IsReported(string? cron)
        {
            var keys = ShippedMqttRender();
            if (cron is null) keys.Remove("CronSchedule"); else keys["CronSchedule"] = cron;

            var errors = Build<MqttAppConfig>(keys, "MqttRenderApp").Validate();

            Assert.Contains(errors, e => e.StartsWith("CronSchedule:"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("abc")]
        [InlineData("00:00:00")]
        [InlineData("-00:05:00")]
        public void ActiveTime_MissingInvalidOrNotPositive_IsReported(string? activeTime)
        {
            var keys = ShippedMqttRender();
            if (activeTime is null) keys.Remove("ActiveTime"); else keys["ActiveTime"] = activeTime;

            var errors = Build<MqttAppConfig>(keys, "MqttRenderApp").Validate();

            Assert.Contains(errors, e => e.StartsWith("ActiveTime:"));
        }

        [Fact]
        public void ActiveTime_InvalidValue_ErrorQuotesTheValue()
        {
            var keys = ShippedMqttRender();
            keys["ActiveTime"] = "abc";

            var errors = Build<MqttAppConfig>(keys, "MqttRenderApp").Validate();

            Assert.Contains(errors, e => e.Contains("'abc'"));
        }

        [Fact]
        public void MqttApp_MissingReadTopic_IsReported()
        {
            var keys = ShippedMqttRender();
            keys.Remove("ReadTopic");

            Assert.Contains(Build<MqttAppConfig>(keys, "MqttRenderApp").Validate(), e => e.StartsWith("ReadTopic:"));
        }

        [Theory]
        [InlineData("StopIdOrigin")]
        [InlineData("StopIdDestination")]
        public void TripTimer_MissingStopId_IsReported(string key)
        {
            var keys = ShippedTripTimer();
            keys.Remove(key);

            Assert.Contains(Build<TripTimerAppConfig>(keys, "TripTimerApp").Validate(), e => e.StartsWith(key + ":"));
        }

        [Fact]
        public void TripTimer_MissingTravelTimes_AreOptional_AndDefaultToZero()
        {
            var keys = ShippedTripTimer();
            keys.Remove("TimeToOrigin");
            keys.Remove("TimeToPrepare");

            var config = Build<TripTimerAppConfig>(keys, "TripTimerApp");

            Assert.Empty(config.Validate());
            Assert.Equal(TimeSpan.Zero, config.TimeToOrigin);
            Assert.Equal(TimeSpan.Zero, config.TimeToPrepare);
        }

        [Theory]
        [InlineData("TimeToOrigin", "soon")]
        [InlineData("TimeToPrepare", "-00:05:00")]
        public void TripTimer_InvalidTravelTime_IsReported(string key, string value)
        {
            var keys = ShippedTripTimer();
            keys[key] = value;

            Assert.Contains(Build<TripTimerAppConfig>(keys, "TripTimerApp").Validate(), e => e.StartsWith(key + ":"));
        }

        [Fact]
        public void EnsureValid_Throws_WithAppTypeDeviceAndKey()
        {
            var keys = ShippedTripTimer();
            keys.Remove("StopIdOrigin");
            var config = Build<TripTimerAppConfig>(keys, "TripTimerApp");

            var ex = Assert.Throws<AppConfigValidationException>(() => config.EnsureValid("awtrix/clock1"));

            Assert.Contains("TripTimerApp", ex.Message);
            Assert.Contains("awtrix/clock1", ex.Message);
            Assert.Contains("StopIdOrigin", ex.Message);
            Assert.Equal("TripTimerApp", ex.AppType);
            Assert.Equal("awtrix/clock1", ex.Device);
            Assert.Single(ex.Errors);
        }

        [Fact]
        public void EnsureValid_ValidConfig_DoesNotThrow()
        {
            Build<TripTimerAppConfig>(ShippedTripTimer(), "TripTimerApp").EnsureValid("awtrix/clock1");
        }
    }
}
```

In `test/Test/Configs/AppConfigConvertValueTests.cs`:
- add `using System.Globalization;` at the top
- replace the whole `GetConfig_MissingKey_NonNullableValueType_ThrowsNullReferenceException` test with:

```csharp
        [Fact]
        public void GetConfig_MissingKey_NonNullableValueType_ReturnsDefault()
        {
            // CR-23: a missing optional value-type key used to throw NullReferenceException at the (T)null cast.
            var sut = new AppConfig();

            Assert.Equal(TimeSpan.Zero, sut.GetConfig<TimeSpan>("Missing"));
            Assert.Equal(0, sut.GetConfig<int>("Missing"));
        }

        [Fact]
        public void GetConfig_WhitespaceValue_NonStringType_ReturnsDefault()
        {
            var sut = new AppConfig();
            sut.SetConfig("Key", "   ");

            Assert.Equal(TimeSpan.Zero, sut.GetConfig<TimeSpan>("Key"));
            Assert.Equal("   ", sut.GetConfig<string>("Key"));
        }

        [Fact]
        public void GetConfig_Double_UsesInvariantCulture_OnCommaDecimalHost()
        {
            var previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            try
            {
                var sut = new AppConfig();
                sut.SetConfig("Key", "3.14");

                Assert.Equal(3.14, sut.GetConfig<double>("Key"));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Fact]
        public void SetConfig_Double_WritesInvariantCulture_OnCommaDecimalHost()
        {
            var previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            try
            {
                var sut = new AppConfig();
                sut.SetConfig("Key", 3.5);

                Assert.Equal("3.5", sut.Config.Get("Key"));
                Assert.Equal(3.5, sut.GetConfig<double>("Key"));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }
```

`test/Test/HostedServices/ConductorConfigValidationTests.cs`:

```csharp
using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using Moq;

namespace Test.HostedServices
{
    /// <summary>
    /// CR-23: invalid typed config is rejected when the app is created, inside Conductor's per-app guard (WS3),
    /// so the app never starts and the other apps on the device still run.
    /// </summary>
    public class ConductorConfigValidationTests
    {
        private const string Device = "awtrix/clock1";

        private static AppConfig App(string type, Dictionary<string, string> keys)
        {
            var app = new AppConfig { Type = type };
            foreach (var kvp in keys)
            {
                app.Config.Add(kvp.Key, kvp.Value);
            }
            return app;
        }

        private static AppConfig Diurnal() => App(AppNames.DiurnalApp, new() { ["0600"] = "Brightness=8" });

        private static Dictionary<string, string> TripTimerKeys() => new()
        {
            ["CronSchedule"] = "10 6 * * 1-5",
            ["ActiveTime"] = "01:00:00",
            ["StopIdOrigin"] = "200060",
            ["StopIdDestination"] = "200070",
            ["TimeToOrigin"] = "00:14:00",
            ["TimeToPrepare"] = "00:08:00",
        };

        private static Dictionary<string, string> MqttKeys(string readTopic) => new()
        {
            ["CronSchedule"] = "0 8 * * *",
            ["ActiveTime"] = "09:00:00",
            ["ReadTopic"] = readTopic,
        };

        private static AwtrixConfig ConfigWith(params AppConfig[] apps) => new()
        {
            Devices = new[] { new DeviceConfig { BaseTopic = Device, Apps = apps.ToList() } },
        };

        [Fact]
        public async Task TripTimerApp_MissingStopIdOrigin_IsSkipped_OtherAppsRun()
        {
            var keys = TripTimerKeys();
            keys.Remove("StopIdOrigin");
            var awtrixService = new Mock<IAwtrixService>();
            var conductor = ConductorTestHelper.Create(ConfigWith(App(AppNames.TripTimerApp, keys), Diurnal()), awtrixService: awtrixService.Object);

            await conductor.StartAsync(CancellationToken.None);
            try
            {
                Assert.Empty(conductor.FindApps(AppNames.TripTimerApp));
                Assert.Single(conductor.FindApps(AppNames.DiurnalApp));
                awtrixService.Verify(s => s.AppClear(It.IsAny<AwtrixAddress>(), AppNames.TripTimerApp), Times.Never);
            }
            finally
            {
                await conductor.StopAsync(CancellationToken.None);
            }
        }

        [Fact]
        public async Task MqttRenderApp_InvalidActiveTime_IsSkipped_OtherAppsRun()
        {
            var keys = MqttKeys("openhab/fronius/grid-surplus");
            keys["ActiveTime"] = "abc";
            var awtrixService = new Mock<IAwtrixService>();
            var conductor = ConductorTestHelper.Create(ConfigWith(App(AppNames.MqttRenderApp, keys), Diurnal()), awtrixService: awtrixService.Object);

            await conductor.StartAsync(CancellationToken.None);
            try
            {
                Assert.Empty(conductor.FindApps(AppNames.MqttRenderApp));
                Assert.Single(conductor.FindApps(AppNames.DiurnalApp));
                awtrixService.Verify(s => s.AppClear(It.IsAny<AwtrixAddress>(), AppNames.MqttRenderApp), Times.Never);
            }
            finally
            {
                await conductor.StopAsync(CancellationToken.None);
            }
        }

        [Fact]
        public async Task ValidScheduledApps_AreRegistered()
        {
            var conductor = ConductorTestHelper.Create(ConfigWith(
                App(AppNames.TripTimerApp, TripTimerKeys()),
                App(AppNames.MqttRenderApp, MqttKeys("openhab/fronius/grid-surplus")),
                App(AppNames.MqttClockRenderApp, MqttKeys("openhab/temperature/room-j"))));

            await conductor.StartAsync(CancellationToken.None);
            try
            {
                Assert.Single(conductor.FindApps(AppNames.TripTimerApp));
                Assert.Single(conductor.FindApps(AppNames.MqttRenderApp));
                Assert.Single(conductor.FindApps(AppNames.MqttClockRenderApp));
            }
            finally
            {
                await conductor.StopAsync(CancellationToken.None);
            }
        }
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Configs.AppConfigValidationTests|FullyQualifiedName~Test.Configs.AppConfigConvertValueTests|FullyQualifiedName~Test.HostedServices.ConductorConfigValidationTests"`
Expected: build FAILS with `CS1061: 'TripTimerAppConfig' does not contain a definition for 'Validate'` and `CS0246: The type or namespace name 'AppConfigValidationException' could not be found`.

- [ ] **Step 3: Implement the exception and the config classes**

`src/api/Apps/Configs/AppConfigValidationException.cs`:

```csharp
namespace AwtrixSharpWeb.Apps.Configs
{
    /// <summary>
    /// Thrown by <see cref="AppConfig.EnsureValid"/>. The message names the app type, device and every invalid key,
    /// so Conductor's per-app guard can log it as the skip reason (CR-23).
    /// </summary>
    public class AppConfigValidationException : Exception
    {
        public AppConfigValidationException(string? appType, string? device, IReadOnlyList<string> errors)
            : base($"Invalid configuration for app '{appType}' on device '{device}': {string.Join("; ", errors)}")
        {
            AppType = appType;
            Device = device;
            Errors = errors;
        }

        public string? AppType { get; }

        public string? Device { get; }

        public IReadOnlyList<string> Errors { get; }
    }
}
```

`src/api/Apps/Configs/AppConfig.cs` (replace the whole file):

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AwtrixSharpWeb.Interfaces;

namespace AwtrixSharpWeb.Apps.Configs
{

    public class AppConfig : IAppConfig
    {
        private List<ValueMap> _valueMaps;

        public AppConfigKeys Config { get; set; }

        [JsonIgnore]
        public string Environment { get; set; }

        public string Type { get; set; }

        /// <summary>
        /// Redirect for now. Reserved for future use if we want to differentiate between two apps of the same type on the same clock
        /// </summary>
        public string Name { get => Type; }

        /// <summary>
        /// Override values received based on a regex 
        /// </summary>
        public List<ValueMap> ValueMaps
        {
            get => _valueMaps;
            set => _valueMaps = value ?? new List<ValueMap>();
        }


        public AppConfig()
        {
            Config = new AppConfigKeys();
            _valueMaps = new List<ValueMap>();
        }

        public static AppConfig Empty(string environment = "")
        {
            var result = new AppConfig();
            result.Environment = environment;
            return result;
        }

        public AppConfig WithName(string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                Type = name;
            }
            return this;
        }

        /// <summary>
        /// Parses the value with the invariant culture. A missing (or, for non-string types, whitespace) value
        /// returns default(T) rather than throwing (CR-23). A malformed value still throws; Validate() catches
        /// that at startup for the keys an app requires.
        /// </summary>
        public T GetConfig<T>(string key)
        {
            var converted = ConvertValue(Config.Get(key), typeof(T));
            return converted is null ? default! : (T)converted;
        }

        public void SetConfig<T>(string key, T value)
        {
            Config[key] = value is IFormattable formattable
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : value?.ToString();
        }

        /// <summary>
        /// One entry per problem, each formatted "{Key}: {reason}". Empty when the config is valid.
        /// </summary>
        public virtual IReadOnlyList<string> Validate() => Array.Empty<string>();

        /// <summary>
        /// Throws <see cref="AppConfigValidationException"/> naming app type, device and keys when <see cref="Validate"/> reports problems.
        /// </summary>
        public void EnsureValid(string? device)
        {
            var errors = Validate();
            if (errors.Count > 0)
            {
                throw new AppConfigValidationException(Type, device, errors);
            }
        }

        protected void ValidateRequired(List<string> errors, string key)
        {
            if (string.IsNullOrWhiteSpace(Config.Get(key)))
            {
                errors.Add($"{key}: required value is missing");
            }
        }

        protected void ValidateTimeSpan(List<string> errors, string key, bool required, bool mustBePositive)
        {
            var raw = Config.Get(key);
            if (string.IsNullOrWhiteSpace(raw))
            {
                if (required)
                {
                    errors.Add($"{key}: required value is missing (expected hh:mm:ss)");
                }
                return;
            }

            if (!TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var value))
            {
                errors.Add($"{key}: '{raw}' is not a valid time span (expected hh:mm:ss)");
                return;
            }

            if (mustBePositive && value <= TimeSpan.Zero)
            {
                errors.Add($"{key}: '{raw}' must be greater than 00:00:00");
            }
            else if (!mustBePositive && value < TimeSpan.Zero)
            {
                errors.Add($"{key}: '{raw}' must not be negative");
            }
        }

        /// <summary>
        /// Find the first ValueMap that matches the input value
        /// </summary>
        /// <param name="input">The input string to match against ValueMatcher patterns</param>
        /// <returns>The first matching ValueMap or null if no match found</returns>
        public ValueMap FindMatchingValueMap(string input)
        {
            return _valueMaps?.FirstOrDefault(map => map.IsMatch(input));
        }

        /// <summary>
        /// Creates a new instance of the specified type and populates its properties from this AppConfig.
        /// </summary>
        public T As<T>() where T : AppConfig, new()
        {
            return CreateFromAppConfig<T>(this);
        }

        /// <summary>
        /// Creates a new instance of the specified type and populates its properties from the source AppConfig.
        /// </summary>
        public static T CreateFromAppConfig<T>(AppConfig source) where T : AppConfig, new()
        {
            T target = new T();

            target.Config = source.Config.Clone();
            target.Environment = source.Environment;
            target.Type = source.Type;

            if (source._valueMaps != null && source._valueMaps.Count > 0)
            {
                target.ValueMaps = new List<ValueMap>(source._valueMaps);
            }

            return target;
        }

        /// <summary>
        /// Converts a string value to the specified type using the invariant culture (CR-23).
        /// </summary>
        private static object ConvertValue(string value, Type targetType)
        {
            if (value is null)
                return null;

            if (targetType == typeof(string))
                return value;

            if (string.IsNullOrWhiteSpace(value))
                return null;

            var invariant = CultureInfo.InvariantCulture;

            if (targetType == typeof(int) || targetType == typeof(int?))
                return int.Parse(value, invariant);

            if (targetType == typeof(long) || targetType == typeof(long?))
                return long.Parse(value, invariant);

            if (targetType == typeof(double) || targetType == typeof(double?))
                return double.Parse(value, invariant);

            if (targetType == typeof(decimal) || targetType == typeof(decimal?))
                return decimal.Parse(value, invariant);

            if (targetType == typeof(bool) || targetType == typeof(bool?))
                return bool.Parse(value);

            if (targetType == typeof(DateTime) || targetType == typeof(DateTime?))
                return DateTime.Parse(value, invariant);

            if (targetType == typeof(TimeSpan) || targetType == typeof(TimeSpan?))
                return TimeSpan.Parse(value, invariant);

            if (targetType == typeof(Guid) || targetType == typeof(Guid?))
                return Guid.Parse(value);

            if (targetType.IsEnum)
                return Enum.Parse(targetType, value, ignoreCase: true);

            if (targetType == typeof(List<ValueMap>))
            {
                try
                {
                    return JsonSerializer.Deserialize<List<ValueMap>>(value);
                }
                catch
                {
                    return new List<ValueMap>();
                }
            }

            throw new NotSupportedException($"Conversion from string to {targetType} is not supported.");
        }
       
        public override string ToString()
        {
            return $"{Type}, Config={Config.ToString()}";
        }
    }
}
```

`src/api/Apps/Configs/ScheduledAppConfig.cs` (replace the whole file):

```csharp
using NCrontab;

namespace AwtrixSharpWeb.Apps.Configs
{
    public class ScheduledAppConfig : AppConfig
    {
        public string CronSchedule 
        { 
            get => GetConfig<string>("CronSchedule");
            set => SetConfig("CronSchedule", value);
        }

        /// <summary>
        /// How long to take over the clock for
        /// </summary>
        public TimeSpan ActiveTime
        { 
            get => GetConfig<TimeSpan>("ActiveTime");
            set => SetConfig("ActiveTime", value);
        }

        public override IReadOnlyList<string> Validate()
        {
            var errors = new List<string>(base.Validate());

            var cron = Config.Get("CronSchedule");
            if (string.IsNullOrWhiteSpace(cron))
            {
                errors.Add("CronSchedule: required value is missing (expected a 5-field cron expression, e.g. '10 6 * * 1-5')");
            }
            else if (CrontabSchedule.TryParse(cron) is null)
            {
                // Same parse options as ScheduledApp.Initialize (5-field)
                errors.Add($"CronSchedule: '{cron}' is not a valid 5-field cron expression");
            }

            ValidateTimeSpan(errors, "ActiveTime", required: true, mustBePositive: true);

            return errors;
        }
    }
}
```

`src/api/Apps/MqttRender/MqttAppConfig.cs` (replace the whole file):

```csharp
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

        public override IReadOnlyList<string> Validate()
        {
            var errors = new List<string>(base.Validate());
            ValidateRequired(errors, "ReadTopic");
            return errors;
        }
    }
}
```

`src/api/Apps/TripTimer/TripTimerAppConfig.cs` (replace the whole file):

```csharp
using AwtrixSharpWeb.Apps.Configs;

namespace AwtrixSharpWeb.Apps.TripTimer
{

    public class TripTimerAppConfig : ScheduledAppConfig 
    {

        public string StopIdOrigin
        {
            get => GetConfig<string>("StopIdOrigin");
            set => SetConfig("StopIdOrigin", value);
        }

        public string StopIdDestination
        {
            get => GetConfig<string>("StopIdDestination");
            set => SetConfig("StopIdDestination", value);
        }


        /// <summary>
        /// Travel time to get to origin (optional, default 00:00:00)
        /// </summary>
        public TimeSpan TimeToOrigin
        {
            get => GetConfig<TimeSpan>("TimeToOrigin");
            set => SetConfig("TimeToOrigin", value);
        }


        /// <summary>
        /// How much time to get ready before leaving (optional, default 00:00:00)
        /// </summary>
        public TimeSpan TimeToPrepare
        {
            get => GetConfig<TimeSpan>("TimeToPrepare");
            set => SetConfig("TimeToPrepare", value);
        }

        public override IReadOnlyList<string> Validate()
        {
            var errors = new List<string>(base.Validate());
            ValidateRequired(errors, "StopIdOrigin");
            ValidateRequired(errors, "StopIdDestination");
            ValidateTimeSpan(errors, "TimeToOrigin", required: false, mustBePositive: false);
            ValidateTimeSpan(errors, "TimeToPrepare", required: false, mustBePositive: false);
            return errors;
        }
    }
}
```

- [ ] **Step 4: Call `EnsureValid` in the Conductor factory**

In `src/api/HostedServices/Conductor.cs` `AppFactory`, add one line directly after each typed-config conversion, **before** the app constructor. Do NOT add it to the `ButtonApp` branch: its synthetic config has no cron.

```csharp
                        var tripTimerConfig = appConfig.As<TripTimerAppConfig>();
                        tripTimerConfig.EnsureValid(device.BaseTopic); // CR-23: rejected inside the per-app creation guard
```

```csharp
                // MqttRenderApp branch
                        var mqttConfig = appConfig.As<MqttAppConfig>();
                        mqttConfig.EnsureValid(device.BaseTopic); // CR-23
```

```csharp
                // MqttClockRenderApp branch
                        var mqttConfig = appConfig.As<MqttAppConfig>();
                        mqttConfig.EnsureValid(device.BaseTopic); // CR-23
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Configs|FullyQualifiedName~Test.HostedServices.ConductorConfigValidationTests|FullyQualifiedName~Test.Domain.TripTimerAppConfigTests"`
Expected: PASS, 0 failed.

- [ ] **Step 6: Update the WS3 startup test that expected an init-failed dispose**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.HostedServices"`
Expected: exactly one failure: the WS3 test recorded in Task 0 Step 3 (a `MqttRenderApp` without `CronSchedule`, verifying `Dismiss(...)` `Times.Once`).

In `test/Test/HostedServices/ConductorStartupTests.cs`, change that test:
- Rename it by replacing its "InitFailed"/"Disposed" suffix with `_IsRejectedAtCreation_AndNeverDisposed`.
- Change its `Dismiss` verification from `Times.Once` to `Times.Never`.
- Add the comment `// WS7 CR-23: config validation now rejects the app before construction, so there is nothing to dispose.`
- Keep its other assertions: StartAsync does not throw, the DiurnalApp on the same device runs, and the MqttRenderApp is not in `FindApps`.

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.HostedServices"`
Expected: PASS, 0 failed.

- [ ] **Step 7: Run the full suite**

Run: `dotnet test`
Expected: 0 failed.

- [ ] **Step 8: Commit**

```bash
git add src/api/Apps/Configs/AppConfigValidationException.cs src/api/Apps/Configs/AppConfig.cs src/api/Apps/Configs/ScheduledAppConfig.cs src/api/Apps/MqttRender/MqttAppConfig.cs src/api/Apps/TripTimer/TripTimerAppConfig.cs src/api/HostedServices/Conductor.cs test/Test/Configs/AppConfigValidationTests.cs test/Test/Configs/AppConfigConvertValueTests.cs test/Test/HostedServices/ConductorConfigValidationTests.cs test/Test/HostedServices/ConductorStartupTests.cs
git commit -m "fix(config): validate typed app config at startup with invariant culture

CR-23: values were parsed lazily with the current culture, and a
missing value-type key threw NullReferenceException at the first cron
wake-up, after which the app never rescheduled. AppConfig now parses
with the invariant culture, returns default(T) for missing values, and
exposes Validate()/EnsureValid(device). Conductor rejects invalid
TripTimer/MqttRender/MqttClockRender configs inside the per-app
creation guard, naming device, app and key; other apps still start.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

### Task 5: Exception handling by environment, `Swagger:Enabled`, optional `Api:Key` (CR-15 pipeline)

**Files:**
- Create: `src/api/Domain/ApiSettings.cs`, `src/api/Middleware/ApiKeyMiddleware.cs`
- Create: `test/Test/Http/HttpPipelineTests.cs`
- Modify: `src/api/Program.cs` (`Main`, new `AddHttpSurface`, `ConfigureHttpPipeline`, `IsSwaggerEnabled`, `RegisterSwagger`)
- Modify: `test/Test/Test.csproj` (add `Microsoft.AspNetCore.TestHost` 10.0.0)

**Interfaces:**
- Produces:
  - `public static void Program.AddHttpSurface(IServiceCollection services, IConfiguration configuration)`
  - `public static void Program.ConfigureHttpPipeline(WebApplication app)`
  - `internal static bool Program.IsSwaggerEnabled(IConfiguration configuration, ILogger logger)`
  - `public class ApiSettings { const string SectionName = "Api"; const string KeyConfigurationKey = "Api:Key"; string? Key; }`
  - `public class ApiKeyMiddleware { const string HeaderName = "X-Api-Key"; Task InvokeAsync(HttpContext); internal static bool IsMatch(string? supplied, string expected); }`

- [ ] **Step 1: Add the test host package**

In `test/Test/Test.csproj`, inside the `<ItemGroup>` with the other `PackageReference`s, add (alphabetical, after `coverlet.collector`):

```xml
    <PackageReference Include="Microsoft.AspNetCore.TestHost" Version="10.0.0" />
```

Run: `dotnet restore test/Test/Test.csproj`
Expected: restore succeeds. If it fails because there is no network access, stop and report; do not substitute another harness.

- [ ] **Step 2: Write the failing pipeline tests**

`test/Test/Http/HttpPipelineTests.cs`:

```csharp
using AwtrixSharpWeb.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using System.Net;

namespace Test.Http
{
    /// <summary>
    /// CR-15: hosts Program.AddHttpSurface/ConfigureHttpPipeline on TestServer with in-memory configuration and
    /// two minimal endpoints. No Awtrix services, broker or network are involved.
    /// </summary>
    public class HttpPipelineTests
    {
        private static async Task<WebApplication> StartAsync(string environment, Dictionary<string, string?>? settings = null)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = environment,
                ContentRootPath = AppContext.BaseDirectory,
            });
            builder.WebHost.UseTestServer();
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());

            AwtrixSharpWeb.Program.AddHttpSurface(builder.Services, builder.Configuration);

            var app = builder.Build();
            AwtrixSharpWeb.Program.ConfigureHttpPipeline(app);
            app.MapGet("/test/ping", () => "pong");
            app.MapGet("/test/boom", new Func<string>(() => throw new InvalidOperationException("secret-detail-ws7")));

            await app.StartAsync();
            return app;
        }

        [Fact]
        public async Task Production_UnhandledException_Returns500ProblemDetails_WithoutExceptionDetail()
        {
            await using var app = await StartAsync("Production");

            var response = await app.GetTestClient().GetAsync("/test/boom");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.DoesNotContain("secret-detail-ws7", body);
            Assert.DoesNotContain("InvalidOperationException", body);
        }

        [Fact]
        public async Task Development_UnhandledException_ShowsExceptionDetail()
        {
            await using var app = await StartAsync("Development");

            var response = await app.GetTestClient().GetAsync("/test/boom");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Contains("secret-detail-ws7", body);
        }

        [Fact]
        public async Task Swagger_IsEnabledByDefault()
        {
            await using var app = await StartAsync("Production");

            var response = await app.GetTestClient().GetAsync("/swagger/v1/swagger.json");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Swagger_CanBeDisabled()
        {
            await using var app = await StartAsync("Production", new() { ["Swagger:Enabled"] = "false" });

            var response = await app.GetTestClient().GetAsync("/swagger/v1/swagger.json");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task Swagger_InvalidEnabledValue_StaysEnabled()
        {
            await using var app = await StartAsync("Production", new() { ["Swagger:Enabled"] = "maybe" });

            var response = await app.GetTestClient().GetAsync("/swagger/v1/swagger.json");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task ApiKey_Unset_RequestsPassWithoutHeader()
        {
            await using var app = await StartAsync("Production");

            var response = await app.GetTestClient().GetAsync("/test/ping");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task ApiKey_Set_MissingHeader_Returns401ProblemDetails()
        {
            await using var app = await StartAsync("Production", new() { ["Api:Key"] = "s3cret" });

            var response = await app.GetTestClient().GetAsync("/test/ping");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }

        [Fact]
        public async Task ApiKey_Set_WrongHeader_Returns401()
        {
            await using var app = await StartAsync("Production", new() { ["Api:Key"] = "s3cret" });
            var client = app.GetTestClient();
            client.DefaultRequestHeaders.Add(ApiKeyMiddleware.HeaderName, "wrong");

            var response = await client.GetAsync("/test/ping");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task ApiKey_Set_CorrectHeader_PassesThrough_IgnoringSurroundingWhitespace()
        {
            await using var app = await StartAsync("Production", new() { ["Api:Key"] = "s3cret\n" });
            var client = app.GetTestClient();
            client.DefaultRequestHeaders.Add(ApiKeyMiddleware.HeaderName, "s3cret");

            var response = await client.GetAsync("/test/ping");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("pong", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task ApiKey_Set_SwaggerJsonStillServed_AndDocumentsHeader()
        {
            await using var app = await StartAsync("Production", new() { ["Api:Key"] = "s3cret" });

            var response = await app.GetTestClient().GetAsync("/swagger/v1/swagger.json");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(ApiKeyMiddleware.HeaderName, body);
        }

        [Theory]
        [InlineData("s3cret", "s3cret", true)]
        [InlineData(" s3cret ", "s3cret", true)]
        [InlineData("S3CRET", "s3cret", false)]
        [InlineData("", "s3cret", false)]
        [InlineData(null, "s3cret", false)]
        public void IsMatch_ComparesTrimmedValuesExactly(string? supplied, string expected, bool match)
        {
            Assert.Equal(match, ApiKeyMiddleware.IsMatch(supplied, expected));
        }
    }
}
```

- [ ] **Step 3: Run the tests and confirm they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Http.HttpPipelineTests"`
Expected: build FAILS with `CS0234: The type or namespace name 'Middleware' does not exist in the namespace 'AwtrixSharpWeb'` and `CS0117: 'Program' does not contain a definition for 'AddHttpSurface'`.

- [ ] **Step 4: Implement settings and middleware**

`src/api/Domain/ApiSettings.cs`:

```csharp
namespace AwtrixSharpWeb.Domain
{
    /// <summary>
    /// Optional HTTP API settings (section "Api", env AWTRIXSHARP_API__KEY). An unset Key means no check,
    /// which is the historical behaviour. Making the key mandatory is a deferred owner decision.
    /// </summary>
    public class ApiSettings
    {
        public const string SectionName = "Api";
        public const string KeyConfigurationKey = "Api:Key";

        public string? Key { get; set; }
    }
}
```

`src/api/Middleware/ApiKeyMiddleware.cs`:

```csharp
using AwtrixSharpWeb.Domain;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace AwtrixSharpWeb.Middleware
{
    /// <summary>
    /// When Api:Key is set, every request reaching this middleware must carry the key in the X-Api-Key header.
    /// Swagger is registered earlier in the pipeline, so its UI and JSON stay reachable. Read per request via
    /// IOptionsMonitor so an appsettings reload applies without restart.
    /// </summary>
    public class ApiKeyMiddleware
    {
        public const string HeaderName = "X-Api-Key";

        private readonly RequestDelegate _next;
        private readonly IOptionsMonitor<ApiSettings> _settings;
        private readonly ILogger<ApiKeyMiddleware> _logger;

        public ApiKeyMiddleware(RequestDelegate next, IOptionsMonitor<ApiSettings> settings, ILogger<ApiKeyMiddleware> logger)
        {
            _next = next;
            _settings = settings;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var expected = _settings.CurrentValue.Key;

            if (string.IsNullOrWhiteSpace(expected) || IsMatch(context.Request.Headers[HeaderName].ToString(), expected))
            {
                await _next(context);
                return;
            }

            _logger.LogWarning(
                "Rejected {Method} {Path} from {RemoteIp}: missing or invalid {Header} header",
                context.Request.Method,
                context.Request.Path,
                context.Connection.RemoteIpAddress,
                HeaderName);

            await Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Missing or invalid API key",
                    detail: $"Send the configured API key in the {HeaderName} header.")
                .ExecuteAsync(context);
        }

        /// <summary>
        /// Trimmed, case-sensitive, constant-time comparison (hashing first equalises lengths).
        /// </summary>
        internal static bool IsMatch(string? supplied, string expected)
        {
            if (string.IsNullOrWhiteSpace(supplied))
            {
                return false;
            }

            var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied.Trim()));
            var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected.Trim()));
            return CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash);
        }
    }
}
```

- [ ] **Step 5: Split the HTTP surface out of `Main`**

In `src/api/Program.cs`:
- add `using AwtrixSharpWeb.Middleware;`
- replace `Main` with:

```csharp
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            var configuration = builder.Configuration;

            var services = builder.Services;

            SetupConfiguration(configuration, services);

            ConfigureLogging(builder);

            AddHttpSurface(services, configuration);

            AddAwtrixServices(services, configuration);

            var app = builder.Build();

            LogStartup(app);

            ConfigureHttpPipeline(app);

            app.Run();
        }

        /// <summary>
        /// MVC, ProblemDetails, API-key options and Swagger generation. Public so tests can host the pipeline.
        /// </summary>
        public static void AddHttpSurface(IServiceCollection services, IConfiguration configuration)
        {
            services.AddControllers();
            services.AddProblemDetails();
            services.Configure<ApiSettings>(configuration.GetSection(ApiSettings.SectionName));
            RegisterSwagger(services, configuration);
        }

        /// <summary>
        /// CR-15: stack traces only in Development; ProblemDetails otherwise. Swagger is optional (default on),
        /// and sits before the optional API-key check so its UI stays reachable.
        /// </summary>
        public static void ConfigureHttpPipeline(WebApplication app)
        {
            if (app.Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler();
            }

            if (IsSwaggerEnabled(app.Configuration, app.Logger))
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.UseMiddleware<ApiKeyMiddleware>();

            app.MapControllers();
        }

        /// <summary>
        /// Swagger:Enabled (AWTRIXSHARP_SWAGGER__ENABLED). Blank or unparseable means enabled, as it always was.
        /// </summary>
        internal static bool IsSwaggerEnabled(IConfiguration configuration, ILogger logger)
        {
            var raw = configuration["Swagger:Enabled"];
            if (string.IsNullOrWhiteSpace(raw))
            {
                return true;
            }

            if (bool.TryParse(raw, out var enabled))
            {
                return enabled;
            }

            logger.LogWarning("Swagger:Enabled value '{Value}' is not true or false; Swagger stays enabled", raw);
            return true;
        }
```

- replace `RegisterSwagger` with:

```csharp
        private static void RegisterSwagger(IServiceCollection services, IConfiguration configuration)
        {
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Awtrix API", Version = "v1" });

                // Enable annotations for Swagger
                c.EnableAnnotations();

                // Let "Try it out" send the optional API key
                if (!string.IsNullOrWhiteSpace(configuration[ApiSettings.KeyConfigurationKey]))
                {
                    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.ApiKey,
                        In = ParameterLocation.Header,
                        Name = ApiKeyMiddleware.HeaderName,
                        Description = "API key configured in Api:Key",
                    });
                    c.AddSecurityRequirement(new OpenApiSecurityRequirement
                    {
                        {
                            new OpenApiSecurityScheme
                            {
                                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" },
                            },
                            Array.Empty<string>()
                        },
                    });
                }
            });
        }
```

- [ ] **Step 6: Run the tests and confirm they pass**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Http.HttpPipelineTests|FullyQualifiedName~Test.CompositionRootTests"`
Expected: PASS, 0 failed.

- [ ] **Step 7: Run the full suite and confirm the unconditional developer page is gone**

Run: `dotnet test`
Expected: 0 failed.

Run: `grep -n "UseDeveloperExceptionPage\|always show swagger" src/api/Program.cs`
Expected: one `UseDeveloperExceptionPage` match, inside `if (app.Environment.IsDevelopment())`; no "always show swagger" comment.

- [ ] **Step 8: Commit**

```bash
git add src/api/Domain/ApiSettings.cs src/api/Middleware/ApiKeyMiddleware.cs src/api/Program.cs test/Test/Test.csproj test/Test/Http/HttpPipelineTests.cs
git commit -m "fix(api): hide stack traces outside Development; optional Swagger toggle and API key

CR-15: DeveloperExceptionPage was always on, returning stack traces to
any LAN client. It now runs only in Development; otherwise
UseExceptionHandler writes a generic ProblemDetails 500.
New optional settings, defaulting to today's behaviour:
- Swagger:Enabled (default true)
- Api:Key (unset = no check; set = X-Api-Key header required, 401
  ProblemDetails otherwise; Swagger UI stays reachable and documents
  the header)
Adds Microsoft.AspNetCore.TestHost 10.0.0 to the test project.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

### Task 6: Controller date input returns 400, parsed with invariant culture (CR-15 input)

**Files:**
- Modify: `src/api/Controllers/TripTimerController.cs` (`TestTimingConfig`)
- Modify: `src/api/Controllers/TripPlannerController.cs` (`GetDepartures`, `GetTrip`, new `TryParseFromDateTime`)
- Modify: `test/Test/Apps/TripTimer/TripTimerControllerTests.cs` (add test)
- Modify: `test/Test/TripPlanner/TripPlannerControllerTests.cs` (replace two tests, add one)

**Interfaces:**
- Consumes: WS3's `TripTimerController(Conductor)` and `Conductor.FindApps(string, string? = null)`; `ConductorTestHelper.Create()`.
- Produces: `BadRequestObjectResult` with `{ message }` for unparseable `departureTime` / `fromDateTime`.

- [ ] **Step 1: Write the failing tests**

Append to the `TripTimerControllerTests` class in `test/Test/Apps/TripTimer/TripTimerControllerTests.cs` (add `using Test.HostedServices;` if missing):

```csharp
        [Fact]
        public void TestTimingConfig_InvalidDepartureTime_ReturnsBadRequest()
        {
            // CR-15: parsing happens before the app lookup, so an empty Conductor is enough
            var sut = new TripTimerController(ConductorTestHelper.Create());

            var result = sut.TestTimingConfig("not-a-date");

            Assert.IsType<BadRequestObjectResult>(result);
        }
```

In `test/Test/TripPlanner/TripPlannerControllerTests.cs`:
- add `using System.Globalization;`
- replace the whole `GetDepartures_InvalidDateTime_Returns500` and `GetTrip_InvalidDateTime_Returns500` tests with:

```csharp
        [Fact]
        public async Task GetDepartures_InvalidDateTime_Returns400()
        {
            var sut = GetSystemUnderTest();

            var result = await sut.GetDepartures("200080", "200060", "not-a-date");

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task GetTrip_InvalidDateTime_Returns400()
        {
            var sut = GetSystemUnderTest();

            var result = await sut.GetTrip("200080", "200060", "not-a-date");

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task GetTrip_ParsesFromDateTimeWithInvariantCulture_RegardlessOfHostCulture()
        {
            // en-AU would read 01/02/2025 as 1 February; invariant reads it as 2 January
            var previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("en-AU");
            try
            {
                SetupTripClientResult(new TripRequestResponse { Journeys = new List<TripRequestResponseJourney>() });
                var sut = GetSystemUnderTest();

                var result = await sut.GetTrip("200080", "200060", "01/02/2025 06:00");

                Assert.IsType<OkObjectResult>(result);
                _mockTripClient.Verify(x => x.Request2Async(
                    It.IsAny<OutputFormat5>(), It.IsAny<CoordOutputFormat4>(), It.IsAny<DepArrMacro>(), "20250102", It.IsAny<string>(),
                    It.IsAny<Type_origin>(), It.IsAny<string>(), It.IsAny<Type_destination>(), It.IsAny<string>(), It.IsAny<int?>(),
                    It.IsAny<Wheelchair?>(), It.IsAny<ExcludedMeans2?>(), It.IsAny<ExclMOT_12?>(), It.IsAny<ExclMOT_22?>(), It.IsAny<ExclMOT_42?>(),
                    It.IsAny<ExclMOT_52?>(), It.IsAny<ExclMOT_72?>(), It.IsAny<ExclMOT_92?>(), It.IsAny<ExclMOT_112?>(), It.IsAny<TfNSWTR?>(),
                    It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool?>(), It.IsAny<int?>(), It.IsAny<BikeProfSpeed?>(), It.IsAny<int?>(),
                    It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>()), Times.Once);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }
```

(If WS6 made the service add a cancellation token or change `Request2Async`'s argument list, copy the matcher list from `SetupTripClientResult` in the same file and keep `"20250102"` as the fourth argument.)

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~TripTimerControllerTests|FullyQualifiedName~TripPlannerControllerTests"`
Expected: FAIL:
- `TestTimingConfig_InvalidDepartureTime_ReturnsBadRequest` fails with `FormatException`.
- Both `*_InvalidDateTime_Returns400` tests fail with `ObjectResult` (500).
- `GetTrip_ParsesFromDateTimeWithInvariantCulture_...` fails its verification (`itdDate` was `20250201`).

- [ ] **Step 3: Implement `TripTimerController.TestTimingConfig`**

In `src/api/Controllers/TripTimerController.cs`:
- add `using System.Globalization;`
- replace the `TestTimingConfig` method with the version below. Keep WS3's exact `NotFound` message if it differs; only the parse block is new.

```csharp
        [HttpPost("test/alarm-timings")]
        public IActionResult TestTimingConfig([FromQuery] string departureTime = "2025-09-01 06:41")
        {
            // CR-15: invariant culture, 400 instead of an unhandled FormatException
            if (!DateTimeOffset.TryParse(departureTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dateTime))
            {
                return BadRequest(new { message = $"departureTime '{departureTime}' is not a valid date/time; use yyyy-MM-dd HH:mm" });
            }

            var app = _conductor
                            .FindApps(AppNames.TripTimerApp)
                            .FirstOrDefault();

            if (app is not TripTimerApp tripTimer)
            {
                return NotFound(new { message = $"App '{AppNames.TripTimerApp}' is not running" });
            }

            var alarmSegments = tripTimer.GetAlarmTime(TripSummary.Factory(dateTime));

            return Ok(alarmSegments);
        }
```

- [ ] **Step 4: Implement `TripPlannerController` parsing**

In `src/api/Controllers/TripPlannerController.cs`:
- add `using System.Globalization;`
- in `GetDepartures`, remove `var fromTimestamp = DateTime.Parse(fromDateTime);` from inside the `try`, and insert before the `try`:

```csharp
            if (!TryParseFromDateTime(fromDateTime, out var fromTimestamp))
            {
                return BadRequest(new { message = $"fromDateTime '{fromDateTime}' is not a valid date/time; use yyyy-MM-ddTHH:mm" });
            }
```

- in `GetTrip`, make exactly the same change (remove the in-`try` `DateTime.Parse` line; insert the same block before the `try`).
- add at the bottom of the class:

```csharp
        /// <summary>
        /// CR-15: invariant culture so the result does not depend on the host's regional settings.
        /// </summary>
        private static bool TryParseFromDateTime(string? value, out DateTime result) =>
            DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
```

If Task 0 Step 4 recorded that WS6 changed the service to take `DateTimeOffset`, use this helper instead (same call sites):

```csharp
        private static bool TryParseFromDateTime(string? value, out DateTimeOffset result) =>
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out result);
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~TripTimerControllerTests|FullyQualifiedName~TripPlannerControllerTests"`
Expected: PASS, 0 failed.

- [ ] **Step 6: Run the full suite and confirm no culture-sensitive parses remain in controllers**

Run: `dotnet test`
Expected: 0 failed.

Run: `grep -rn "DateTime.Parse(\|DateTimeOffset.Parse(\|\.First()" src/api/Controllers`
Expected: no matches.

- [ ] **Step 7: Commit**

```bash
git add src/api/Controllers/TripTimerController.cs src/api/Controllers/TripPlannerController.cs test/Test/Apps/TripTimer/TripTimerControllerTests.cs test/Test/TripPlanner/TripPlannerControllerTests.cs
git commit -m "fix(api): return 400 for invalid date query parameters, parse invariantly

CR-15: TripTimer test/alarm-timings threw an unhandled FormatException
and TripPlanner departures/trip returned 500 for a bad fromDateTime;
all three parsed with the host culture. They now use the invariant
culture and return 400 with { message }. Routes, parameters and
success responses are unchanged.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

## Self-review (completed by planner)

| Spec item | Task |
|---|---|
| D1 precedence, release note | 1 (note in spec §7) |
| D2 TfNSW/Slack/DataSettings via IConfiguration + fallback; SlackUserId; warnings | 2 |
| D3 case-insensitive dictionaries | 3 |
| D4 invariant culture, default(T), invariant SetConfig | 4 |
| D5 Validate/EnsureValid rules; Conductor creation guard; WS3 test update | 4 |
| D6 exception handling by environment | 5 |
| D7 Swagger:Enabled | 5 |
| D8 Api:Key middleware + Swagger security scheme | 5 |
| D9 controller 400s | 6 |
| Task 0 verification of WS1-WS6 shapes | 0 |

- **Placeholder scan:** every code step has full code. The only conditional instructions are Task 0-driven adaptations, and each gives exact replacement code.
- **Type consistency:**
  - `SlackSettings.WithEnvironmentFallback()`, `DataSettings.DataDirectoryKey`, `ApiKeyMiddleware.HeaderName`, `ApiSettings.KeyConfigurationKey`, `AppConfigValidationException.AppType/Device/Errors` and `ConductorTestHelper.Create(..., slackSettings:)` are used with the same names in every task.
  - `EnsureValid(string? device)` is called with `device.BaseTopic`.
