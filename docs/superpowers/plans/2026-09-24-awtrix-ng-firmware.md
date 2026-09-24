# AWTRIX NG Firmware Profile Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a device in `appsettings.json` declare `"Firmware": "NG"` and receive the same apps as an AWTRIX 3 device, with all NG addressing and payload translation isolated behind one seam.

**Architecture:** `AwtrixService` asks `AwtrixFirmware.For(address.Firmware)` to build an `AwtrixRequest(Address, Method, Payload)`; publishers become dumb transports that send a request. `Awtrix3Firmware` reproduces today's behaviour byte-for-byte; `NgFirmware` uses NG topics/endpoints and `NgPayloadTranslator`/`NgSettingsTranslator` convert the AWTRIX 3 vocabulary that `AwtrixAppMessage`/`AwtrixSettings` still carry. `ButtonApp` asks the profile for its button topics.

**Tech Stack:** .NET 10, ASP.NET Core, System.Text.Json, xunit 2.9, Moq. No new packages.

**Spec:** `docs/superpowers/specs/2026-09-24-awtrix-ng-firmware-design.md`

## Global Constraints

- AWTRIX 3 behaviour is unchanged: every existing topic/URL/body assertion in `test/Test` must still pass with only the mechanical `Publish(url, payload)` → `Publish(AwtrixRequest)` change in fakes.
- `Firmware` defaults to `Awtrix3`; existing `appsettings.json` binds unchanged.
- Config vocabulary stays AWTRIX 3 (`ValueMaps` keys, Diurnal `Brightness=`/`GlobalTextColor=`); the NG profile translates.
- Publishers never throw (`AwtrixPublisher.Publish` contract). `AwtrixService.SafePublish` stays.
- Numbers are always formatted/parsed with `CultureInfo.InvariantCulture`.
- Nothing may run the app in the Development environment (real broker). Only `ASPNETCORE_ENVIRONMENT=Simulator` is allowed, and PlatformIO is not installed on this machine, so no task runs the simulator.
- Commit after every task; commits end with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
- Run all tests with `dotnet test test/Test/Test.csproj` from the repo root.

## Review Focus

1. An NG HTTP `BaseTopic` copied in AWTRIX 3 style (`http://host/api`) must not produce `http://host/api/api/v1/...` — pinned in Task 6 (`HttpRoot_StripsApiSuffixes`).
2. An `AwtrixAppMessage` whose `text` is a fragment array with bare `FF0000` colours (what `TripTimerApp` sends) must reach NG as `#FF0000` — pinned in Task 4 (`Text_FragmentArray_RenamesKeysAndNormalisesColours`).
3. A notification carrying `lifetime` or an app carrying `hold` must not be rejected wholesale by NG's 422 — pinned in Task 4 (kind-filter tests).
4. A ValueMap key with no NG equivalent (`TopText`) must not silently vanish forever — pinned in Task 7 (`AppUpdate_NgDroppedKey_LogsWarningOncePerDeviceAndKey`).
5. `ASPNETCORE_ENVIRONMENT=Simulator` must not load user secrets (the real broker) — pinned in Task 8 (`Simulator_DoesNotLoadUserSecrets`).

---

### Task 1: `AwtrixFirmwareKind` and `AwtrixAddress.Firmware`

**Files:**
- Create: `src/api/Services/Firmware/AwtrixFirmwareKind.cs`
- Modify: `src/api/Domain/AwtrixAddress.cs`
- Test: `test/Test/Domain/AwtrixAddressTests.cs`

**Interfaces:**
- Produces: `enum AwtrixFirmwareKind { Awtrix3, NG }` in namespace `AwtrixSharpWeb.Services.Firmware`; `AwtrixAddress.Firmware` property (default `Awtrix3`).

- [ ] **Step 1: Write the failing tests** (append inside the class in `AwtrixAddressTests.cs`; add `using AwtrixSharpWeb.Services.Firmware;`, `using Microsoft.Extensions.Configuration;`)

```csharp
        [Fact]
        public void Firmware_DefaultsToAwtrix3()
        {
            Assert.Equal(AwtrixFirmwareKind.Awtrix3, new AwtrixAddress().Firmware);
        }

        [Theory]
        [InlineData("NG", AwtrixFirmwareKind.NG)]
        [InlineData("ng", AwtrixFirmwareKind.NG)]
        [InlineData("Awtrix3", AwtrixFirmwareKind.Awtrix3)]
        public void Firmware_BindsFromConfigurationCaseInsensitively(string value, AwtrixFirmwareKind expected)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Awtrix:Devices:0:BaseTopic"] = "awtrix/clock2",
                    ["Awtrix:Devices:0:Firmware"] = value,
                })
                .Build();

            var config = configuration.GetSection("Awtrix").Get<AwtrixConfig>()!;

            Assert.Equal(expected, config.Devices[0].Firmware);
        }

        [Fact]
        public void Firmware_AbsentFromConfiguration_DefaultsToAwtrix3()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Awtrix:Devices:0:BaseTopic"] = "awtrix/clock1" })
                .Build();

            var config = configuration.GetSection("Awtrix").Get<AwtrixConfig>()!;

            Assert.Equal(AwtrixFirmwareKind.Awtrix3, config.Devices[0].Firmware);
        }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~AwtrixAddressTests"`
Expected: build error, `AwtrixFirmwareKind` not found.

- [ ] **Step 3: Implement**

`src/api/Services/Firmware/AwtrixFirmwareKind.cs`:
```csharp
namespace AwtrixSharpWeb.Services.Firmware
{
    /// <summary>
    /// Which firmware a device runs. Selects the addressing and payload dialect (see AwtrixFirmware.For).
    /// Awtrix3 is the default and is transitional; NG is https://blueforcer.github.io/awtrix-ng/.
    /// </summary>
    public enum AwtrixFirmwareKind
    {
        Awtrix3 = 0,
        NG = 1,
    }
}
```

In `AwtrixAddress.cs` add `using AwtrixSharpWeb.Services.Firmware;` and, after `BaseTopic`:
```csharp
        /// <summary>
        /// Firmware dialect of the device. Defaults to Awtrix3 so existing configs bind unchanged.
        /// For NG, BaseTopic is the device's mqttPrefix (MQTT) or its root URL such as http://192.168.1.51 (HTTP).
        /// </summary>
        public AwtrixFirmwareKind Firmware { get; set; } = AwtrixFirmwareKind.Awtrix3;
```

- [ ] **Step 4: Run tests**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~AwtrixAddressTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/api/Services/Firmware/AwtrixFirmwareKind.cs src/api/Domain/AwtrixAddress.cs test/Test/Domain/AwtrixAddressTests.cs
git commit -m "feat(firmware): Firmware setting on AwtrixAddress, default Awtrix3"
```

---

### Task 2: `AwtrixRequest` and request-based publishers

**Files:**
- Create: `src/api/Services/Firmware/AwtrixRequest.cs`
- Modify: `src/api/Services/AwtrixPublisher.cs`, `src/api/Services/HttpPublisher.cs`, `src/api/Services/MqttPublisher.cs`
- Modify tests: `test/Test/Services/FakePublishers.cs`, `test/Test/Services/AwtrixPublisherTests.cs`, `test/Test/Services/HttpPublisherTests.cs`

**Interfaces:**
- Produces: `sealed record AwtrixRequest(string Address, HttpMethod Method, string Payload, IReadOnlyList<string>? DroppedKeys = null)` with `static AwtrixRequest Post(string address, string payload)`. `AwtrixPublisher.Publish(AwtrixRequest)` is the abstract primitive; `Publish(string url, string payload)` remains as a non-virtual POST convenience. `AwtrixPublisher.Publish(string, AwtrixAppMessage?)`, `ToJson` and `BuildCustomAppUrl` are **kept in this task** (removed in Task 3) so `AwtrixService` still compiles.

- [ ] **Step 1: Write the failing tests**

Replace `RecordingPublisher` in `AwtrixPublisherTests.cs`:
```csharp
    public class RecordingPublisher : AwtrixPublisher
    {
        public string? LastUrl { get; private set; }
        public string? LastPayload { get; private set; }
        public HttpMethod? LastMethod { get; private set; }
        public bool ReturnValue { get; set; } = true;

        public RecordingPublisher() : base(NullLogger<RecordingPublisher>.Instance)
        {
        }

        public override Task<bool> Publish(AwtrixRequest request)
        {
            LastUrl = request.Address;
            LastPayload = request.Payload;
            LastMethod = request.Method;
            return Task.FromResult(ReturnValue);
        }
    }
```
Add `using AwtrixSharpWeb.Services.Firmware;` and these tests to `AwtrixPublisherTests`:
```csharp
        [Fact]
        public async Task Publish_UrlAndPayload_IsAPostRequest()
        {
            var publisher = new RecordingPublisher();

            await publisher.Publish("topic/x", "{}");

            Assert.Equal(HttpMethod.Post, publisher.LastMethod);
            Assert.Equal("topic/x", publisher.LastUrl);
            Assert.Equal("{}", publisher.LastPayload);
        }

        [Fact]
        public void AwtrixRequest_Post_DefaultsMethodAndEmptyDroppedKeys()
        {
            var request = AwtrixRequest.Post("a/b", "x");

            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Empty(request.DroppedKeys);
        }
```

Update `FakePublishers.cs`: both fakes replace `public override Task<bool> Publish(string url, string payload)` with
```csharp
        public HttpMethod? LastMethod { get; private set; }

        public override Task<bool> Publish(AwtrixRequest request)
        {
            LastUrl = request.Address;
            LastPayload = request.Payload;
            LastMethod = request.Method;
            PublishCallCount++;
            if (ThrowOnPublish != null)
            {
                throw ThrowOnPublish;
            }
            return Task.FromResult(ReturnValue);
        }
```
(add `using AwtrixSharpWeb.Services.Firmware;`).

Add to `HttpPublisherTests.cs` (add `using AwtrixSharpWeb.Services.Firmware;`, `using System.Net.Http;`):
```csharp
        [Fact]
        public async Task Publish_DeleteWithEmptyPayload_SendsNoBody()
        {
            var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK);
            var publisher = CreatePublisher(handler, out _);

            var result = await publisher.Publish(new AwtrixRequest("http://localhost:8080/api/v1/apps/x", HttpMethod.Delete, string.Empty));

            Assert.True(result);
            var request = Assert.Single(handler.Requests);
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Null(request.Content);
        }

        [Fact]
        public async Task Publish_PutWithPayload_SendsJsonBodyWithPut()
        {
            var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK);
            var publisher = CreatePublisher(handler, out _);

            await publisher.Publish(new AwtrixRequest("http://localhost:8080/api/v1/apps/pushed/x", HttpMethod.Put, "{\"text\":\"hi\"}"));

            var request = Assert.Single(handler.Requests);
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
            Assert.Equal("{\"text\":\"hi\"}", handler.RequestBodies[0]);
        }

        [Fact]
        public async Task Publish_PostWithEmptyPayload_StillSendsEmptyJsonBody()
        {
            // AWTRIX 3 clears a custom app with an empty POST body; that must keep working
            var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK);
            var publisher = CreatePublisher(handler, out _);

            await publisher.Publish("http://192.168.1.50/api/custom?name=x", string.Empty);

            var request = Assert.Single(handler.Requests);
            Assert.NotNull(request.Content);
            Assert.Equal(string.Empty, handler.RequestBodies[0]);
        }

        [Fact]
        public async Task Publish_4xxWithBody_ReturnsFalse()
        {
            var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                Content = new StringContent("{\"error\":\"unknown field color\"}")
            }));
            var publisher = CreatePublisher(handler, out _);

            var result = await publisher.Publish(new AwtrixRequest("http://localhost:8080/api/v1/apps/pushed/x", HttpMethod.Put, "{}"));

            Assert.False(result);
        }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet build`
Expected: errors, `AwtrixRequest` not found / no override of `Publish(AwtrixRequest)`.

- [ ] **Step 3: Implement**

`src/api/Services/Firmware/AwtrixRequest.cs`:
```csharp
namespace AwtrixSharpWeb.Services.Firmware
{
    /// <summary>
    /// A fully addressed message for a device. Address is an MQTT topic or an absolute URL; Method only
    /// matters over HTTP. Payload is string.Empty for bodiless requests. DroppedKeys lists message keys a
    /// firmware profile could not express (so the caller can warn); empty for AWTRIX 3.
    /// </summary>
    public sealed record AwtrixRequest(string Address, HttpMethod Method, string Payload, IReadOnlyList<string>? DroppedKeys = null)
    {
        public IReadOnlyList<string> DroppedKeys { get; init; } = DroppedKeys ?? Array.Empty<string>();

        public static AwtrixRequest Post(string address, string payload) => new(address, HttpMethod.Post, payload);
    }
}
```

`AwtrixPublisher.cs`: add `using AwtrixSharpWeb.Services.Firmware;`; replace the abstract `Publish(string url, string payload)` and the message overload with:
```csharp
        /// <summary>
        /// Transport primitive. Contract: MUST NOT throw. Returns true only when the transport
        /// confirmed the hand-off (HTTP 2xx / MQTT client publish completed); every failure is
        /// logged by the implementation and reported as false.
        /// </summary>
        public abstract Task<bool> Publish(AwtrixRequest request);

        /// <summary>POST convenience for callers that only have a topic/URL and body (diagnostics, tests).</summary>
        public Task<bool> Publish(string url, string payload) => Publish(AwtrixRequest.Post(url, payload ?? string.Empty));

        public async Task<bool> Publish(string url, AwtrixAppMessage? message)
        {
            var json = ToJson(message);
            var publisherType = this.GetType().Name;
            Logger.LogDebug("{publisherType} Publishing to {url} with payload: {json}", publisherType, url, json);
            return await Publish(url, json);
        }
```
Keep `ToJson` and `BuildCustomAppUrl` for now.

`MqttPublisher.cs`:
```csharp
        public override async Task<bool> Publish(AwtrixRequest request)
        {
            try
            {
                return await _mqttConnector.PublishAsync(request.Address, request.Payload);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "MQTT publish to {Topic} failed", request.Address);
                return false;
            }
        }
```

`HttpPublisher.cs` (add `using AwtrixSharpWeb.Services.Firmware;`):
```csharp
        private const int MaxLoggedBodyLength = 512;

        public override async Task<bool> Publish(AwtrixRequest request)
        {
            var url = request.Address;
            try
            {
                var client = _httpClientFactory.CreateClient(HttpClientName);
                using var httpRequest = new HttpRequestMessage(request.Method, url);
                // DELETE carries no body; every other verb sends JSON, even when empty (AWTRIX 3 clears an app with an empty POST)
                if (request.Method != HttpMethod.Delete || request.Payload.Length > 0)
                {
                    httpRequest.Content = new StringContent(request.Payload, Encoding.UTF8, "application/json");
                }
                using var response = await client.SendAsync(httpRequest);

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }

                var body = await ReadBodyForLog(response);
                Logger.LogWarning("HTTP {Method} to {Url} returned {StatusCode}{Body}", request.Method, url, (int)response.StatusCode, body);
                return false;
            }
            catch (Exception ex)
            {
                // Routine when a device is offline: log type + message, not the stack trace
                Logger.LogWarning("HTTP publish to {Url} failed: {ErrorType}: {Error}", url, ex.GetType().Name, ex.Message);
                return false;
            }
        }

        /// <summary>NG answers 4xx with a JSON body naming the offending field; surface it, truncated.</summary>
        private static async Task<string> ReadBodyForLog(HttpResponseMessage response)
        {
            try
            {
                var body = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(body))
                {
                    return string.Empty;
                }
                return ": " + (body.Length > MaxLoggedBodyLength ? body.Substring(0, MaxLoggedBodyLength) + "…" : body);
            }
            catch
            {
                return string.Empty;
            }
        }
```
Remove the old `Publish(string url, string payload)` override from `HttpPublisher`; keep `BuildCustomAppUrl` for now.

- [ ] **Step 4: Run all tests**

Run: `dotnet test test/Test/Test.csproj`
Expected: PASS (existing `Publish(url, payload)` callers use the convenience overload).

- [ ] **Step 5: Commit**

```bash
git add src/api/Services test/Test/Services
git commit -m "refactor(publishers): publish an AwtrixRequest with an HTTP method; log 4xx bodies"
```

---

### Task 3: `IAwtrixFirmware`, `Awtrix3Firmware`, `AwtrixFirmware.For`, service wiring

**Files:**
- Create: `src/api/Services/Firmware/IAwtrixFirmware.cs`, `src/api/Services/Firmware/Awtrix3Firmware.cs`, `src/api/Services/Firmware/AwtrixFirmware.cs`
- Modify: `src/api/Services/AwtrixService.cs`, `src/api/Services/AwtrixPublisher.cs` (remove `ToJson`, `Publish(url, message)`, `BuildCustomAppUrl`), `src/api/Services/HttpPublisher.cs` (remove `BuildCustomAppUrl`)
- Test: create `test/Test/Services/Firmware/Awtrix3FirmwareTests.cs`; modify `test/Test/Services/AwtrixPublisherTests.cs` (delete `ToJson_*`, `Publish_With*Message*`, `BuildCustomAppUrl_*` tests)

**Interfaces:**
- Produces:
```csharp
public interface IAwtrixFirmware
{
    AwtrixFirmwareKind Kind { get; }
    AwtrixRequest AppUpdate(AwtrixAddress address, string appName, AwtrixAppMessage message);
    AwtrixRequest AppClear(AwtrixAddress address, string appName);
    AwtrixRequest Notify(AwtrixAddress address, AwtrixAppMessage message);
    AwtrixRequest Dismiss(AwtrixAddress address);
    AwtrixRequest Settings(AwtrixAddress address, AwtrixSettings settings);
    AwtrixRequest PlayRtttl(AwtrixAddress address, string rtttl);
    string ButtonTopic(AwtrixAddress address, Button button);
}
public static class AwtrixFirmware { public static IAwtrixFirmware For(AwtrixFirmwareKind kind); }
```
`For(AwtrixFirmwareKind.NG)` throws `NotSupportedException` until Task 6 replaces it.

- [ ] **Step 1: Write the failing tests** — `test/Test/Services/Firmware/Awtrix3FirmwareTests.cs`

```csharp
using AwtrixSharpWeb.Apps.MqttRender;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Services.Firmware;

namespace Test.Services.Firmware
{
    /// <summary>Pins the AWTRIX 3 dialect exactly as the service sent it before the firmware seam existed.</summary>
    public class Awtrix3FirmwareTests
    {
        private static readonly IAwtrixFirmware Sut = AwtrixFirmware.For(AwtrixFirmwareKind.Awtrix3);
        private static readonly AwtrixAddress Mqtt = new() { BaseTopic = "awtrix/clock1" };
        private static readonly AwtrixAddress Http = new() { BaseTopic = "http://192.168.1.50/api" };

        [Fact]
        public void Kind_IsAwtrix3() => Assert.Equal(AwtrixFirmwareKind.Awtrix3, Sut.Kind);

        [Fact]
        public void AppUpdate_Mqtt()
        {
            var r = Sut.AppUpdate(Mqtt, "TripTimerApp", new AwtrixAppMessage().SetText("42"));
            Assert.Equal("awtrix/clock1/custom/TripTimerApp", r.Address);
            Assert.Equal(HttpMethod.Post, r.Method);
            Assert.Equal("{\"text\":\"42\"}", r.Payload);
            Assert.Empty(r.DroppedKeys);
        }

        [Fact]
        public void AppUpdate_Http_EscapesName()
        {
            var r = Sut.AppUpdate(Http, "My App", new AwtrixAppMessage().SetText("42"));
            Assert.Equal("http://192.168.1.50/api/custom?name=My%20App", r.Address);
            Assert.Equal(HttpMethod.Post, r.Method);
        }

        [Fact]
        public void AppClear_IsEmptyPostToSameAddress()
        {
            Assert.Equal(("awtrix/clock1/custom/X", HttpMethod.Post, ""), Deconstruct(Sut.AppClear(Mqtt, "X")));
            Assert.Equal(("http://192.168.1.50/api/custom?name=X", HttpMethod.Post, ""), Deconstruct(Sut.AppClear(Http, "X")));
        }

        [Fact]
        public void Notify_And_Dismiss()
        {
            Assert.Equal(("awtrix/clock1/notify", HttpMethod.Post, "{\"text\":\"hi\"}"), Deconstruct(Sut.Notify(Mqtt, new AwtrixAppMessage().SetText("hi"))));
            Assert.Equal(("awtrix/clock1/notify/dismiss", HttpMethod.Post, ""), Deconstruct(Sut.Dismiss(Mqtt)));
            Assert.Equal(("http://192.168.1.50/api/notify/dismiss", HttpMethod.Post, ""), Deconstruct(Sut.Dismiss(Http)));
        }

        [Fact]
        public void Settings_SerialisesRawKeys()
        {
            var r = Sut.Settings(Mqtt, new AwtrixSettings().SetBrightness(8).SetGlobalTextColor("#FFFFFF"));
            Assert.Equal("awtrix/clock1/settings", r.Address);
            Assert.Equal("{\"BRI\":\"8\",\"TCOL\":\"#FFFFFF\"}", r.Payload);
        }

        [Fact]
        public void PlayRtttl_IsRawString()
        {
            Assert.Equal(("awtrix/clock1/rtttl", HttpMethod.Post, "a:d=4:c"), Deconstruct(Sut.PlayRtttl(Mqtt, "a:d=4:c")));
        }

        [Theory]
        [InlineData(Button.Left, "awtrix/clock1/stats/buttonLeft")]
        [InlineData(Button.Select, "awtrix/clock1/stats/buttonSelect")]
        [InlineData(Button.Right, "awtrix/clock1/stats/buttonRight")]
        public void ButtonTopic(Button button, string expected) => Assert.Equal(expected, Sut.ButtonTopic(Mqtt, button));

        [Fact]
        public void For_ReturnsSameInstance() => Assert.Same(Sut, AwtrixFirmware.For(AwtrixFirmwareKind.Awtrix3));

        private static (string, HttpMethod, string) Deconstruct(AwtrixRequest r) => (r.Address, r.Method, r.Payload);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet build`
Expected: `IAwtrixFirmware` not found.

- [ ] **Step 3: Implement**

`IAwtrixFirmware.cs`:
```csharp
using AwtrixSharpWeb.Apps.MqttRender;
using AwtrixSharpWeb.Domain;

namespace AwtrixSharpWeb.Services.Firmware
{
    /// <summary>
    /// One firmware dialect: where each operation goes (topic or URL + HTTP method) and what its body is.
    /// Implementations are pure and stateless; obtain them from <see cref="AwtrixFirmware.For"/>.
    /// </summary>
    public interface IAwtrixFirmware
    {
        AwtrixFirmwareKind Kind { get; }
        AwtrixRequest AppUpdate(AwtrixAddress address, string appName, AwtrixAppMessage message);
        AwtrixRequest AppClear(AwtrixAddress address, string appName);
        AwtrixRequest Notify(AwtrixAddress address, AwtrixAppMessage message);
        AwtrixRequest Dismiss(AwtrixAddress address);
        AwtrixRequest Settings(AwtrixAddress address, AwtrixSettings settings);
        AwtrixRequest PlayRtttl(AwtrixAddress address, string rtttl);
        /// <summary>MQTT topic the device publishes 0/1 button state on. Only meaningful for MQTT addresses.</summary>
        string ButtonTopic(AwtrixAddress address, Button button);
    }
}
```

`Awtrix3Firmware.cs`:
```csharp
using AwtrixSharpWeb.Apps.MqttRender;
using AwtrixSharpWeb.Domain;

namespace AwtrixSharpWeb.Services.Firmware
{
    /// <summary>
    /// AWTRIX 3 dialect (https://blueforcer.github.io/awtrix3/#/api). Transitional: delete this class and the
    /// enum member when no AWTRIX 3 devices remain. Everything is a POST; clears are empty bodies.
    /// </summary>
    public sealed class Awtrix3Firmware : IAwtrixFirmware
    {
        public AwtrixFirmwareKind Kind => AwtrixFirmwareKind.Awtrix3;

        public AwtrixRequest AppUpdate(AwtrixAddress address, string appName, AwtrixAppMessage message)
            => AwtrixRequest.Post(CustomAppAddress(address, appName), message.ToJson());

        public AwtrixRequest AppClear(AwtrixAddress address, string appName)
            => AwtrixRequest.Post(CustomAppAddress(address, appName), string.Empty);

        public AwtrixRequest Notify(AwtrixAddress address, AwtrixAppMessage message)
            => AwtrixRequest.Post(address.BaseTopic + "/notify", message.ToJson());

        public AwtrixRequest Dismiss(AwtrixAddress address)
            => AwtrixRequest.Post(address.BaseTopic + "/notify/dismiss", string.Empty);

        public AwtrixRequest Settings(AwtrixAddress address, AwtrixSettings settings)
            => AwtrixRequest.Post(address.BaseTopic + "/settings", settings.ToJson());

        public AwtrixRequest PlayRtttl(AwtrixAddress address, string rtttl)
            => AwtrixRequest.Post(address.BaseTopic + "/rtttl", rtttl);

        public string ButtonTopic(AwtrixAddress address, Button button)
            => $"{address.BaseTopic}/stats/button{button}";

        /// <summary>MQTT: {base}/custom/{app}. HTTP: POST http://[ip]/api/custom?name=[app].</summary>
        private static string CustomAppAddress(AwtrixAddress address, string appName)
        {
            return address.IsHttp
                ? $"{address.BaseTopic.TrimEnd('/')}/custom?name={Uri.EscapeDataString(appName)}"
                : $"{address.BaseTopic}/custom/{appName}";
        }
    }
}
```

`AwtrixFirmware.cs`:
```csharp
namespace AwtrixSharpWeb.Services.Firmware
{
    /// <summary>Static registry of firmware profiles; they are stateless so one instance each is enough.</summary>
    public static class AwtrixFirmware
    {
        private static readonly IAwtrixFirmware Awtrix3 = new Awtrix3Firmware();

        public static IAwtrixFirmware For(AwtrixFirmwareKind kind)
        {
            return kind switch
            {
                AwtrixFirmwareKind.Awtrix3 => Awtrix3,
                _ => throw new NotSupportedException($"Firmware '{kind}' is not supported"),
            };
        }
    }
}
```

`AwtrixService.cs`: add `using AwtrixSharpWeb.Services.Firmware;`. Each operation becomes (shown for `AppUpdate`; do the same for `Set`, `PlayRtttl`, `AppClear`, `Notify`, `Dismiss`):
```csharp
        public Task<bool> AppUpdate(AwtrixAddress awtrixAddress, string appName, AwtrixAppMessage message)
        {
            if (IsNull(awtrixAddress, nameof(awtrixAddress), nameof(AppUpdate)) || IsNull(message, nameof(message), nameof(AppUpdate)))
            {
                return Task.FromResult(false);
            }

            return SafePublish(awtrixAddress, f => f.AppUpdate(awtrixAddress, appName, message));
        }
```
and `SafePublish` becomes:
```csharp
        /// <summary>
        /// Builds the request with the device's firmware profile and hands it to the transport for its address.
        /// Publishers must not throw, but if one (or a profile) does the failure is contained here.
        /// </summary>
        private async Task<bool> SafePublish(AwtrixAddress address, Func<IAwtrixFirmware, AwtrixRequest> build)
        {
            var baseTopic = address.BaseTopic;
            try
            {
                var firmware = AwtrixFirmware.For(address.Firmware);
                var request = build(firmware);
                var publisher = ResolvePublisher(baseTopic);
                _logger.LogDebug("{Publisher} {Method} {Address} payload: {Payload}", publisher.GetType().Name, request.Method, request.Address, request.Payload);
                var delivered = await publisher.Publish(request);
                if (!delivered)
                {
                    _logger.LogDebug("Publish via {Publisher} for {BaseTopic} was not delivered", publisher.GetType().Name, baseTopic);
                }
                return delivered;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Publisher threw for {BaseTopic}; treating as not delivered", baseTopic);
                return false;
            }
        }
```
`Notify` keeps its blank-text → `Dismiss(awtrixAddress)` rule before building.

Remove from `AwtrixPublisher`: `ToJson`, `Publish(string url, AwtrixAppMessage? message)`, `BuildCustomAppUrl`. Remove `BuildCustomAppUrl` from `HttpPublisher` (and its `using AwtrixSharpWeb.Domain;` if now unused). Delete the corresponding tests in `AwtrixPublisherTests` (`ToJson_NullMessage_ReturnsEmptyString`, `ToJson_WithMessage_DelegatesToMessageToJson`, `Publish_WithNullMessage_PublishesEmptyStringPayload`, `Publish_WithMessage_PublishesSerializedJson`, `Publish_ReturnsUnderlyingImplementationResult`, `BuildCustomAppUrl_Default_UsesMqttTopicForm`). Grep `test/Test` for `BuildCustomAppUrl` and remove any other references (e.g. in `HttpPublisherTests`).

- [ ] **Step 4: Run all tests**

Run: `dotnet test test/Test/Test.csproj`
Expected: PASS. `AwtrixServicePublishTests` must pass unmodified: they pin the AWTRIX 3 dialect.

- [ ] **Step 5: Commit**

```bash
git add src/api/Services test/Test/Services
git commit -m "refactor(firmware): route AwtrixService through an IAwtrixFirmware profile; Awtrix3Firmware pins today's dialect"
```

---

### Task 4: `NgPayloadTranslator`

**Files:**
- Create: `src/api/Services/Firmware/NgPayloadTranslator.cs`, `src/api/Services/Firmware/NgColour.cs`
- Test: `test/Test/Services/Firmware/NgPayloadTranslatorTests.cs`, `test/Test/Services/Firmware/NgColourTests.cs`

**Interfaces:**
- Produces:
```csharp
public enum NgPayloadKind { App, Notification }
public sealed record NgTranslation(string Json, IReadOnlyList<string> DroppedKeys);
public static class NgPayloadTranslator { public static NgTranslation Translate(AwtrixAppMessage message, NgPayloadKind kind); }
internal static class NgColour { public static object? Normalise(string value); /* "#RRGGBB" string or int[] or null */ }
```

- [ ] **Step 1: Write the failing tests**

`NgColourTests.cs`:
```csharp
using AwtrixSharpWeb.Services.Firmware;

namespace Test.Services.Firmware
{
    public class NgColourTests
    {
        [Theory]
        [InlineData("#FF0000", "#FF0000")]
        [InlineData("FF0000", "#FF0000")]
        [InlineData("ff00aa", "#ff00aa")]
        [InlineData("#F00", "#F00")]
        public void Normalise_HexStrings(string input, string expected) => Assert.Equal(expected, NgColour.Normalise(input));

        [Fact]
        public void Normalise_CommaSeparatedInts_BecomesArray()
        {
            Assert.Equal(new[] { 255, 0, 8 }, Assert.IsType<int[]>(NgColour.Normalise("255,0,8")));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("red")]
        [InlineData("1,2")]
        [InlineData("#GG0000")]
        public void Normalise_Invalid_ReturnsNull(string input) => Assert.Null(NgColour.Normalise(input));
    }
}
```

`NgPayloadTranslatorTests.cs`:
```csharp
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Services.Firmware;

namespace Test.Services.Firmware
{
    /// <summary>One test per row of the spec's key table, plus the precedence/kind/ordering rules.</summary>
    public class NgPayloadTranslatorTests
    {
        private static string App(AwtrixAppMessage m) => NgPayloadTranslator.Translate(m, NgPayloadKind.App).Json;
        private static string Notification(AwtrixAppMessage m) => NgPayloadTranslator.Translate(m, NgPayloadKind.Notification).Json;

        [Fact]
        public void Text_PlainString() => Assert.Equal("{\"text\":\"42\"}", App(new AwtrixAppMessage().SetText("42")));

        [Fact]
        public void Text_FragmentArray_RenamesKeysAndNormalisesColours()
        {
            var m = new AwtrixAppMessage().SetText("[{\"t\":\"07:10\",\"c\":\"00FF00\"},{\"t\":\" ->07:24\",\"c\":\"#FF0000\"}]");
            Assert.Equal("{\"text\":[{\"text\":\"07:10\",\"color\":\"#00FF00\"},{\"text\":\" ->07:24\",\"color\":\"#FF0000\"}]}", App(m));
        }

        [Fact]
        public void Text_MalformedFragmentArray_IsSentAsPlainString()
        {
            var m = new AwtrixAppMessage().SetText("[not json");
            Assert.Equal("{\"text\":\"[not json\"}", App(m));
        }

        [Theory]
        [InlineData(0, "inherit")]
        [InlineData(1, "upper")]
        [InlineData(2, "asTyped")]
        public void TextCase_Words(int value, string expected) => Assert.Equal($"{{\"textCase\":\"{expected}\"}}", App(new AwtrixAppMessage().SetTextCase(value)));

        [Fact]
        public void TextCase_OutOfRange_IsDroppedAndReported()
        {
            var t = NgPayloadTranslator.Translate(new AwtrixAppMessage().SetTextCase(7), NgPayloadKind.App);
            Assert.Equal("{}", t.Json);
            Assert.Equal(new[] { "textCase" }, t.DroppedKeys);
        }

        [Fact]
        public void TopText_IsDroppedAndReported()
        {
            var t = NgPayloadTranslator.Translate(new AwtrixAppMessage().SetTopText(true), NgPayloadKind.App);
            Assert.Equal("{}", t.Json);
            Assert.Equal(new[] { "topText" }, t.DroppedKeys);
        }

        [Fact]
        public void Hold_And_Stack_NotificationOnly()
        {
            var m = new AwtrixAppMessage().SetHold().SetStack(false);
            Assert.Equal("{\"hold\":true,\"stack\":false}", Notification(m));
            var app = NgPayloadTranslator.Translate(m, NgPayloadKind.App);
            Assert.Equal("{}", app.Json);
            Assert.Empty(app.DroppedKeys); // dropped for kind, not unsupported
        }

        [Fact]
        public void Lifetime_AppOnly_Milliseconds()
        {
            var m = new AwtrixAppMessage().SetLifetime(120).SetLifetimeMode(1);
            Assert.Equal("{\"lifetimeExpiry\":\"mark\",\"lifetimeMs\":120000}", App(m));
            Assert.Equal("{}", Notification(m));
        }

        [Fact]
        public void LifetimeMode_Zero_IsRemove() => Assert.Equal("{\"lifetimeExpiry\":\"remove\"}", App(new AwtrixAppMessage().SetLifetimeMode(0)));

        [Fact]
        public void TextOffset_Center_Color_Background()
        {
            var m = new AwtrixAppMessage().SetTextOffset(3).SetCenter(false).SetColor("00FF00").SetBackground("#000011");
            Assert.Equal("{\"backgroundColor\":\"#000011\",\"textCenter\":false,\"textColor\":\"#00FF00\",\"textOffsetX\":3}", App(m));
        }

        [Fact]
        public void Gradient_BecomesPaletteAndOverridesColor()
        {
            var m = new AwtrixAppMessage().SetColor("#FFFFFF").SetGradient(new[] { new[] { 255, 0, 0 }, new[] { 0, 0, 255 } });
            Assert.Equal("{\"palette\":[[255,0,0],[0,0,255]],\"textColor\":\"palette\"}", App(m));
        }

        [Fact]
        public void Rainbow_True_BecomesRainbowPalette()
        {
            Assert.Equal("{\"palette\":\"Rainbow\",\"textColor\":\"palette\"}", App(new AwtrixAppMessage().SetRainbow()));
        }

        [Fact]
        public void Rainbow_False_IsSilentlyDropped()
        {
            var t = NgPayloadTranslator.Translate(new AwtrixAppMessage().SetRainbow(false).SetColor("#FFFFFF"), NgPayloadKind.App);
            Assert.Equal("{\"textColor\":\"#FFFFFF\"}", t.Json);
            Assert.Empty(t.DroppedKeys);
        }

        [Fact]
        public void PalettePrecedence_GradientOverRainbowOverEffectPalette()
        {
            var m = new AwtrixAppMessage().SetEffectPalette("Ocean").SetRainbow().SetGradient(new[] { new[] { 1, 2, 3 } });
            Assert.Equal("{\"palette\":[[1,2,3]],\"textColor\":\"palette\"}", App(m));
            Assert.Equal("{\"palette\":\"Rainbow\",\"textColor\":\"palette\"}", App(new AwtrixAppMessage().SetEffectPalette("Ocean").SetRainbow()));
        }

        [Fact]
        public void EffectPalette_Alone_DoesNotForceTextColorPalette()
        {
            Assert.Equal("{\"palette\":\"Ocean\"}", App(new AwtrixAppMessage().SetEffectPalette("Ocean")));
        }

        [Fact]
        public void BlinkAndFade_AreMilliseconds()
        {
            Assert.Equal("{\"textBlinkMs\":500,\"textFadeMs\":250}", App(new AwtrixAppMessage().SetBlinkText(500).SetFadeText(250)));
        }

        [Theory]
        [InlineData(0, "fixed")]
        [InlineData(1, "pushOnce")]
        [InlineData(2, "push")]
        public void PushIcon_ToIconMode(int value, string expected) => Assert.Equal($"{{\"iconMode\":\"{expected}\"}}", App(new AwtrixAppMessage().SetPushIcon(value)));

        [Fact]
        public void Icon_And_Duration()
        {
            Assert.Equal("{\"durationMs\":300000,\"icon\":\"1667\"}", App(new AwtrixAppMessage().SetIcon("1667").SetDuration(300)));
        }

        [Fact]
        public void Charts()
        {
            var m = new AwtrixAppMessage().SetLine(new[] { 1, 2 }).SetBar(new[] { 3, 4 }).SetAutoscale(true);
            Assert.Equal("{\"barChart\":[3,4],\"chartAutoscale\":true,\"lineChart\":[1,2]}", App(m));
        }

        [Fact]
        public void Progress()
        {
            var m = new AwtrixAppMessage().SetProgress(50).SetProgressC(new[] { 255, 0, 0 }).SetProgressBC(new[] { 0, 0, 255 });
            Assert.Equal("{\"progress\":50,\"progressColor\":[255,0,0],\"progressTrackColor\":[0,0,255]}", App(m));
        }

        [Fact]
        public void ScrollSpeed_BecomesScrollObject() => Assert.Equal("{\"scroll\":{\"speed\":80}}", App(new AwtrixAppMessage().SetScrollSpeed(80)));

        [Fact]
        public void Effects_And_Overlay()
        {
            var m = new AwtrixAppMessage().SetEffect("Matrix").SetEffectSpeed(3).SetEffectBlend(true).SetOverlay("rain");
            Assert.Equal("{\"effect\":\"Matrix\",\"effectSpeed\":3,\"overlay\":\"rain\",\"paletteBlend\":true}", App(m));
        }

        [Fact]
        public void UnknownKey_IsDroppedAndReported()
        {
            var m = new AwtrixAppMessage { ["bogus"] = "1" };
            var t = NgPayloadTranslator.Translate(m, NgPayloadKind.App);
            Assert.Equal("{}", t.Json);
            Assert.Equal(new[] { "bogus" }, t.DroppedKeys);
        }

        [Fact]
        public void UnparsableValue_IsDroppedAndReported()
        {
            var m = new AwtrixAppMessage { ["duration"] = "abc", ["color"] = "purple" };
            var t = NgPayloadTranslator.Translate(m, NgPayloadKind.App);
            Assert.Equal("{}", t.Json);
            Assert.Equal(new[] { "color", "duration" }, t.DroppedKeys.OrderBy(k => k).ToArray());
        }

        [Fact]
        public void TextIsFirst_ThenSortedKeys()
        {
            var m = new AwtrixAppMessage().SetProgress(1).SetIcon("1").SetText("x");
            Assert.Equal("{\"text\":\"x\",\"icon\":\"1\",\"progress\":1}", App(m));
        }

        [Fact]
        public void EmptyMessage_IsEmptyObject() => Assert.Equal("{}", App(new AwtrixAppMessage()));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet build`
Expected: `NgPayloadTranslator` not found.

- [ ] **Step 3: Implement**

`NgColour.cs`:
```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using AwtrixSharpWeb.Domain;

namespace AwtrixSharpWeb.Services.Firmware
{
    /// <summary>
    /// NG accepts "#RRGGBB", "#RGB" and [r,g,b]. AWTRIX 3 configs also carry bare "RRGGBB" (TripTimerApp fragments)
    /// and "r,g,b" strings (progressC), so normalise those. Anything else is null: the caller drops the key.
    /// </summary>
    internal static class NgColour
    {
        private static readonly Regex Hex = new("^#?([0-9a-fA-F]{6}|[0-9a-fA-F]{3})$", RegexOptions.Compiled);

        public static object? Normalise(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            var match = Hex.Match(trimmed);
            if (match.Success)
            {
                return "#" + match.Groups[1].Value;
            }

            if (AwtrixAppMessage.TryParseIntArray(trimmed, out var rgb) && rgb.Length == 3)
            {
                return rgb;
            }

            return null;
        }
    }
}
```

`NgPayloadTranslator.cs`:
```csharp
using System.Globalization;
using System.Text.Json;
using AwtrixSharpWeb.Domain;

namespace AwtrixSharpWeb.Services.Firmware
{
    public enum NgPayloadKind { App, Notification }

    /// <summary>Json is the NG body; DroppedKeys are message keys with no NG equivalent or an unparsable value.</summary>
    public sealed record NgTranslation(string Json, IReadOnlyList<string> DroppedKeys);

    /// <summary>
    /// Converts an AWTRIX 3 vocabulary <see cref="AwtrixAppMessage"/> into an NG pushed-app or notification body
    /// (https://blueforcer.github.io/awtrix-ng/reference/payload/). Values are emitted typed because NG validates
    /// types and rejects the whole payload on any unknown key. Keys are emitted text-first then sorted so tests
    /// can compare strings. Rules (see spec): palette precedence gradient > rainbow > effectPalette; a palette
    /// forces textColor "palette"; hold/stack are notification-only; lifetime* are app-only.
    /// </summary>
    public static class NgPayloadTranslator
    {
        private static readonly HashSet<string> NotificationOnly = new(StringComparer.Ordinal) { "hold", "stack" };
        private static readonly HashSet<string> AppOnly = new(StringComparer.Ordinal) { "lifetime", "lifetimeMode" };

        public static NgTranslation Translate(AwtrixAppMessage message, NgPayloadKind kind)
        {
            var output = new Dictionary<string, object?>(StringComparer.Ordinal);
            var dropped = new List<string>();
            object? palette = null;
            var palettePriority = -1; // 0 effectPalette, 1 rainbow, 2 gradient
            var scrollSpeed = (int?)null;

            void SetPalette(object value, int priority)
            {
                if (priority > palettePriority)
                {
                    palette = value;
                    palettePriority = priority;
                }
            }

            foreach (var (key, raw) in message)
            {
                if (kind == NgPayloadKind.App && NotificationOnly.Contains(key)) continue;
                if (kind == NgPayloadKind.Notification && AppOnly.Contains(key)) continue;

                var ok = key switch
                {
                    "text" => Put(output, "text", TranslateText(raw)),
                    "textCase" => PutEnum(output, "textCase", raw, "inherit", "upper", "asTyped"),
                    "hold" => PutBool(output, "hold", raw),
                    "stack" => PutBool(output, "stack", raw),
                    "textOffset" => PutInt(output, "textOffsetX", raw),
                    "center" => PutBool(output, "textCenter", raw),
                    "color" => Put(output, "textColor", NgColour.Normalise(raw)),
                    "background" => Put(output, "backgroundColor", NgColour.Normalise(raw)),
                    "gradient" => TryMatrix(raw, out var matrix) && Do(() => SetPalette(matrix, 2)),
                    "rainbow" => TryBool(raw, out var rainbow) && (!rainbow || Do(() => SetPalette("Rainbow", 1))),
                    "effectPalette" => !string.IsNullOrWhiteSpace(raw) && Do(() => SetPalette(raw, 0)),
                    "blinkText" => PutInt(output, "textBlinkMs", raw, allowDouble: true),
                    "fadeText" => PutInt(output, "textFadeMs", raw, allowDouble: true),
                    "icon" => Put(output, "icon", raw),
                    "pushIcon" => PutEnum(output, "iconMode", raw, "fixed", "pushOnce", "push"),
                    "duration" => PutSecondsAsMs(output, "durationMs", raw),
                    "lifetime" => PutSecondsAsMs(output, "lifetimeMs", raw),
                    "lifetimeMode" => PutEnum(output, "lifetimeExpiry", raw, "remove", "mark"),
                    "line" => PutIntArray(output, "lineChart", raw),
                    "bar" => PutIntArray(output, "barChart", raw),
                    "autoscale" => PutBool(output, "chartAutoscale", raw),
                    "overlay" => Put(output, "overlay", raw),
                    "progress" => PutInt(output, "progress", raw),
                    "progressC" => Put(output, "progressColor", NgColour.Normalise(raw)),
                    "progressBC" => Put(output, "progressTrackColor", NgColour.Normalise(raw)),
                    "scrollSpeed" => TryInt(raw, out var speed) && Do(() => scrollSpeed = speed),
                    "effect" => Put(output, "effect", raw),
                    "effectSpeed" => PutNumber(output, "effectSpeed", raw),
                    "effectBlend" => PutBool(output, "paletteBlend", raw),
                    _ => false, // topText and anything unknown
                };

                if (!ok)
                {
                    dropped.Add(key);
                }
            }

            if (palette != null)
            {
                output["palette"] = palette;
                output["textColor"] = palettePriority >= 1 ? "palette" : output.GetValueOrDefault("textColor");
                if (output["textColor"] == null) output.Remove("textColor");
            }
            if (scrollSpeed.HasValue)
            {
                output["scroll"] = new Dictionary<string, object> { ["speed"] = scrollSpeed.Value };
            }

            var ordered = output
                .OrderBy(kvp => kvp.Key == "text" ? "" : kvp.Key, StringComparer.Ordinal)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            return new NgTranslation(JsonSerializer.Serialize(ordered), dropped);
        }

        private static bool Do(Action action) { action(); return true; }

        private static bool Put(Dictionary<string, object?> output, string key, object? value)
        {
            if (value == null) return false;
            output[key] = value;
            return true;
        }

        private static bool TryInt(string? raw, out int value) => int.TryParse(raw?.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
        private static bool TryBool(string? raw, out bool value) => bool.TryParse(raw?.Trim(), out value);
        private static bool TryMatrix(string? raw, out int[][] value) => AwtrixAppMessage.TryParseIntMatrix(raw, out value);

        private static bool PutInt(Dictionary<string, object?> output, string key, string? raw, bool allowDouble = false)
        {
            if (TryInt(raw, out var i)) return Put(output, key, i);
            if (allowDouble && double.TryParse(raw?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d))
                return Put(output, key, (int)Math.Round(d));
            return false;
        }

        private static bool PutNumber(Dictionary<string, object?> output, string key, string? raw)
        {
            if (TryInt(raw, out var i)) return Put(output, key, i);
            return double.TryParse(raw?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) && Put(output, key, d);
        }

        private static bool PutBool(Dictionary<string, object?> output, string key, string? raw) => TryBool(raw, out var b) && Put(output, key, b);

        private static bool PutIntArray(Dictionary<string, object?> output, string key, string? raw) => AwtrixAppMessage.TryParseIntArray(raw, out var a) && Put(output, key, a);

        private static bool PutSecondsAsMs(Dictionary<string, object?> output, string key, string? raw) => TryInt(raw, out var s) && Put(output, key, (long)s * 1000);

        private static bool PutEnum(Dictionary<string, object?> output, string key, string? raw, params string[] names)
            => TryInt(raw, out var i) && i >= 0 && i < names.Length && Put(output, key, names[i]);

        /// <summary>AWTRIX 3 fragments are [{"t","c"}]; NG wants [{"text","color"}]. Anything unparsable is sent as the plain string.</summary>
        private static object? TranslateText(string? raw)
        {
            if (raw == null) return null;
            if (!raw.StartsWith("[")) return raw;
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return raw;
                var fragments = new List<Dictionary<string, object?>>();
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var fragment = new Dictionary<string, object?>();
                    if (el.TryGetProperty("t", out var t)) fragment["text"] = t.GetString();
                    else if (el.TryGetProperty("text", out var t2)) fragment["text"] = t2.GetString();
                    if (el.TryGetProperty("c", out var c)) fragment["color"] = NgColour.Normalise(c.GetString());
                    else if (el.TryGetProperty("color", out var c2)) fragment["color"] = NgColour.Normalise(c2.GetString());
                    if (fragment["color"] is null) fragment.Remove("color");
                    fragments.Add(fragment);
                }
                return fragments;
            }
            catch (JsonException)
            {
                return raw;
            }
        }
    }
}
```
Note `fragment["color"] is null` throws `KeyNotFoundException` when the key was never added; use `fragment.TryGetValue("color", out var col) && col is null` instead. Fix that when implementing.

`AwtrixAppMessage.TryParseIntArray`/`TryParseIntMatrix` are `internal`; the translator is in the same assembly, so no change.

- [ ] **Step 4: Run tests**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Services.Firmware"`
Expected: PASS. If `EffectPalette_Alone_DoesNotForceTextColorPalette` fails, check the palette block: only priority ≥ 1 (rainbow/gradient) forces `textColor: "palette"`.

- [ ] **Step 5: Commit**

```bash
git add src/api/Services/Firmware test/Test/Services/Firmware
git commit -m "feat(ng): translate AWTRIX 3 message vocabulary into typed NG payloads"
```

---

### Task 5: `NgSettingsTranslator`

**Files:**
- Create: `src/api/Services/Firmware/NgSettingsTranslator.cs`
- Test: `test/Test/Services/Firmware/NgSettingsTranslatorTests.cs`

**Interfaces:**
- Produces: `public static class NgSettingsTranslator { public static NgTranslation Translate(AwtrixSettings settings); }`

- [ ] **Step 1: Write the failing tests**

```csharp
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Services.Firmware;

namespace Test.Services.Firmware
{
    public class NgSettingsTranslatorTests
    {
        [Fact]
        public void Brightness_And_TextColor()
        {
            var t = NgSettingsTranslator.Translate(new AwtrixSettings().SetBrightness(8).SetGlobalTextColor("FFFFFF"));
            Assert.Equal("{\"brightness\":8,\"textColor\":\"#FFFFFF\"}", t.Json);
            Assert.Empty(t.DroppedKeys);
        }

        [Fact]
        public void UnknownKey_IsDroppedAndReported()
        {
            var t = NgSettingsTranslator.Translate(new AwtrixSettings { ["ABRI"] = "true" });
            Assert.Equal("{}", t.Json);
            Assert.Equal(new[] { "ABRI" }, t.DroppedKeys);
        }

        [Fact]
        public void UnparsableValue_IsDroppedAndReported()
        {
            var t = NgSettingsTranslator.Translate(new AwtrixSettings { ["BRI"] = "bright", ["TCOL"] = "white" });
            Assert.Equal("{}", t.Json);
            Assert.Equal(new[] { "BRI", "TCOL" }, t.DroppedKeys.OrderBy(k => k).ToArray());
        }
    }
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet build` → `NgSettingsTranslator` not found.

- [ ] **Step 3: Implement**

```csharp
using System.Globalization;
using System.Text.Json;
using AwtrixSharpWeb.Domain;

namespace AwtrixSharpWeb.Services.Firmware
{
    /// <summary>AWTRIX 3 settings keys → NG PATCH /api/v1/settings keys. BRI→brightness, TCOL→textColor; others dropped.</summary>
    public static class NgSettingsTranslator
    {
        public static NgTranslation Translate(AwtrixSettings settings)
        {
            var output = new SortedDictionary<string, object>(StringComparer.Ordinal);
            var dropped = new List<string>();

            foreach (var (key, raw) in settings)
            {
                var ok = key switch
                {
                    "BRI" => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bri) && Add(output, "brightness", bri),
                    "TCOL" => NgColour.Normalise(raw) is { } colour && Add(output, "textColor", colour),
                    _ => false,
                };
                if (!ok)
                {
                    dropped.Add(key);
                }
            }

            return new NgTranslation(JsonSerializer.Serialize(output), dropped);
        }

        private static bool Add(SortedDictionary<string, object> output, string key, object value)
        {
            output[key] = value;
            return true;
        }
    }
}
```

- [ ] **Step 4: Run tests** — `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~NgSettingsTranslatorTests"` → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/api/Services/Firmware/NgSettingsTranslator.cs test/Test/Services/Firmware/NgSettingsTranslatorTests.cs
git commit -m "feat(ng): translate BRI/TCOL settings to NG keys"
```

---

### Task 6: `NgFirmware` addressing

**Files:**
- Create: `src/api/Services/Firmware/NgFirmware.cs`
- Modify: `src/api/Services/Firmware/AwtrixFirmware.cs`
- Test: `test/Test/Services/Firmware/NgFirmwareTests.cs`

**Interfaces:**
- Consumes: `NgPayloadTranslator.Translate`, `NgSettingsTranslator.Translate`, `AwtrixRequest`.
- Produces: `AwtrixFirmware.For(AwtrixFirmwareKind.NG)` returns `NgFirmware`; `NgFirmware.HttpRoot(string baseTopic)` (internal static) normalises the root.

- [ ] **Step 1: Write the failing tests**

```csharp
using AwtrixSharpWeb.Apps.MqttRender;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Services.Firmware;

namespace Test.Services.Firmware
{
    /// <summary>Pins the NG addressing table from the spec (reference/mqtt, reference/http).</summary>
    public class NgFirmwareTests
    {
        private static readonly IAwtrixFirmware Sut = AwtrixFirmware.For(AwtrixFirmwareKind.NG);
        private static readonly AwtrixAddress Mqtt = new() { BaseTopic = "awtrix/clock2", Firmware = AwtrixFirmwareKind.NG };
        private static readonly AwtrixAddress Http = new() { BaseTopic = "http://localhost:8080", Firmware = AwtrixFirmwareKind.NG };

        [Fact]
        public void Kind_IsNG() => Assert.Equal(AwtrixFirmwareKind.NG, Sut.Kind);

        [Fact]
        public void AppUpdate_Mqtt()
        {
            var r = Sut.AppUpdate(Mqtt, "TripTimerApp", new AwtrixAppMessage().SetText("42").SetDuration(5));
            Assert.Equal("awtrix/clock2/cmd/apps/pushed/TripTimerApp", r.Address);
            Assert.Equal("{\"text\":\"42\",\"durationMs\":5000}", r.Payload);
        }

        [Fact]
        public void AppUpdate_Http_IsPutWithEscapedName()
        {
            var r = Sut.AppUpdate(Http, "My App", new AwtrixAppMessage().SetText("42"));
            Assert.Equal("http://localhost:8080/api/v1/apps/pushed/My%20App", r.Address);
            Assert.Equal(HttpMethod.Put, r.Method);
        }

        [Fact]
        public void AppUpdate_ReportsDroppedKeys()
        {
            var r = Sut.AppUpdate(Mqtt, "X", new AwtrixAppMessage().SetTopText(true).SetText("x"));
            Assert.Equal(new[] { "topText" }, r.DroppedKeys);
        }

        [Fact]
        public void AppClear_Mqtt_IsEmptyPayload()
        {
            var r = Sut.AppClear(Mqtt, "X");
            Assert.Equal("awtrix/clock2/cmd/apps/pushed/X", r.Address);
            Assert.Equal(string.Empty, r.Payload);
        }

        [Fact]
        public void AppClear_Http_IsDelete()
        {
            var r = Sut.AppClear(Http, "X");
            Assert.Equal("http://localhost:8080/api/v1/apps/X", r.Address);
            Assert.Equal(HttpMethod.Delete, r.Method);
            Assert.Equal(string.Empty, r.Payload);
        }

        [Fact]
        public void Notify_UsesNotificationKind()
        {
            var m = new AwtrixAppMessage().SetText("hi").SetHold().SetLifetime(5);
            var mqtt = Sut.Notify(Mqtt, m);
            Assert.Equal("awtrix/clock2/cmd/notify", mqtt.Address);
            Assert.Equal("{\"text\":\"hi\",\"hold\":true}", mqtt.Payload);
            var http = Sut.Notify(Http, m);
            Assert.Equal("http://localhost:8080/api/v1/notifications", http.Address);
            Assert.Equal(HttpMethod.Post, http.Method);
        }

        [Fact]
        public void Dismiss()
        {
            Assert.Equal(("awtrix/clock2/cmd/notify/dismiss", HttpMethod.Post, ""), D(Sut.Dismiss(Mqtt)));
            Assert.Equal(("http://localhost:8080/api/v1/notifications/active", HttpMethod.Delete, ""), D(Sut.Dismiss(Http)));
        }

        [Fact]
        public void Settings()
        {
            var s = new AwtrixSettings().SetBrightness(8);
            Assert.Equal(("awtrix/clock2/cmd/settings", HttpMethod.Post, "{\"brightness\":8}"), D(Sut.Settings(Mqtt, s)));
            Assert.Equal(("http://localhost:8080/api/v1/settings", HttpMethod.Patch, "{\"brightness\":8}"), D(Sut.Settings(Http, s)));
            Assert.Equal(new[] { "ABRI" }, Sut.Settings(Mqtt, new AwtrixSettings { ["ABRI"] = "1" }).DroppedKeys);
        }

        [Fact]
        public void PlayRtttl_WrapsInJson()
        {
            Assert.Equal(("awtrix/clock2/cmd/audio/play", HttpMethod.Post, "{\"rtttl\":\"a:d=4:c\"}"), D(Sut.PlayRtttl(Mqtt, "a:d=4:c")));
            Assert.Equal(("http://localhost:8080/api/v1/audio/play", HttpMethod.Post, "{\"rtttl\":\"a:d=4:c\"}"), D(Sut.PlayRtttl(Http, "a:d=4:c")));
        }

        [Theory]
        [InlineData(Button.Left, "awtrix/clock2/state/buttons/left")]
        [InlineData(Button.Select, "awtrix/clock2/state/buttons/select")]
        [InlineData(Button.Right, "awtrix/clock2/state/buttons/right")]
        public void ButtonTopic(Button button, string expected) => Assert.Equal(expected, Sut.ButtonTopic(Mqtt, button));

        [Theory]
        [InlineData("http://host", "http://host")]
        [InlineData("http://host/", "http://host")]
        [InlineData("http://host/api", "http://host")]
        [InlineData("http://host/api/", "http://host")]
        [InlineData("http://host/api/v1", "http://host")]
        [InlineData("http://host:8080/api/v1/", "http://host:8080")]
        [InlineData("HTTP://Host/API", "HTTP://Host")]
        public void HttpRoot_StripsApiSuffixes(string input, string expected) => Assert.Equal(expected, NgFirmware.HttpRoot(input));

        [Fact]
        public void For_ReturnsSameInstance() => Assert.Same(Sut, AwtrixFirmware.For(AwtrixFirmwareKind.NG));

        private static (string, HttpMethod, string) D(AwtrixRequest r) => (r.Address, r.Method, r.Payload);
    }
}
```
Also add `[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Test")]` only if not already present: check `grep -rn InternalsVisibleTo src/api`. If absent, make `HttpRoot` `public static` instead.

- [ ] **Step 2: Run to verify failure** — `dotnet build` → `NgFirmware` not found.

- [ ] **Step 3: Implement**

`NgFirmware.cs`:
```csharp
using System.Text.Json;
using AwtrixSharpWeb.Apps.MqttRender;
using AwtrixSharpWeb.Domain;

namespace AwtrixSharpWeb.Services.Firmware
{
    /// <summary>
    /// AWTRIX NG dialect. MQTT: {prefix}/cmd/... (https://blueforcer.github.io/awtrix-ng/reference/mqtt/).
    /// HTTP: {root}/api/v1/... with real verbs (https://blueforcer.github.io/awtrix-ng/reference/http/).
    /// BaseTopic is the device's mqttPrefix or its root URL; a trailing /api or /api/v1 is tolerated.
    /// </summary>
    public sealed class NgFirmware : IAwtrixFirmware
    {
        public AwtrixFirmwareKind Kind => AwtrixFirmwareKind.NG;

        public AwtrixRequest AppUpdate(AwtrixAddress address, string appName, AwtrixAppMessage message)
        {
            var t = NgPayloadTranslator.Translate(message, NgPayloadKind.App);
            return address.IsHttp
                ? new AwtrixRequest($"{HttpRoot(address.BaseTopic)}/api/v1/apps/pushed/{Uri.EscapeDataString(appName)}", HttpMethod.Put, t.Json, t.DroppedKeys)
                : new AwtrixRequest($"{address.BaseTopic}/cmd/apps/pushed/{appName}", HttpMethod.Post, t.Json, t.DroppedKeys);
        }

        public AwtrixRequest AppClear(AwtrixAddress address, string appName)
        {
            // MQTT keeps delete-by-empty-payload; HTTP needs an explicit DELETE (an empty PUT is a 422)
            return address.IsHttp
                ? new AwtrixRequest($"{HttpRoot(address.BaseTopic)}/api/v1/apps/{Uri.EscapeDataString(appName)}", HttpMethod.Delete, string.Empty)
                : AwtrixRequest.Post($"{address.BaseTopic}/cmd/apps/pushed/{appName}", string.Empty);
        }

        public AwtrixRequest Notify(AwtrixAddress address, AwtrixAppMessage message)
        {
            var t = NgPayloadTranslator.Translate(message, NgPayloadKind.Notification);
            return address.IsHttp
                ? new AwtrixRequest($"{HttpRoot(address.BaseTopic)}/api/v1/notifications", HttpMethod.Post, t.Json, t.DroppedKeys)
                : new AwtrixRequest($"{address.BaseTopic}/cmd/notify", HttpMethod.Post, t.Json, t.DroppedKeys);
        }

        public AwtrixRequest Dismiss(AwtrixAddress address)
        {
            return address.IsHttp
                ? new AwtrixRequest($"{HttpRoot(address.BaseTopic)}/api/v1/notifications/active", HttpMethod.Delete, string.Empty)
                : AwtrixRequest.Post($"{address.BaseTopic}/cmd/notify/dismiss", string.Empty);
        }

        public AwtrixRequest Settings(AwtrixAddress address, AwtrixSettings settings)
        {
            var t = NgSettingsTranslator.Translate(settings);
            return address.IsHttp
                ? new AwtrixRequest($"{HttpRoot(address.BaseTopic)}/api/v1/settings", HttpMethod.Patch, t.Json, t.DroppedKeys)
                : new AwtrixRequest($"{address.BaseTopic}/cmd/settings", HttpMethod.Post, t.Json, t.DroppedKeys);
        }

        public AwtrixRequest PlayRtttl(AwtrixAddress address, string rtttl)
        {
            var payload = JsonSerializer.Serialize(new Dictionary<string, string> { ["rtttl"] = rtttl });
            return address.IsHttp
                ? new AwtrixRequest($"{HttpRoot(address.BaseTopic)}/api/v1/audio/play", HttpMethod.Post, payload)
                : AwtrixRequest.Post($"{address.BaseTopic}/cmd/audio/play", payload);
        }

        public string ButtonTopic(AwtrixAddress address, Button button)
            => $"{address.BaseTopic}/state/buttons/{button.ToString().ToLowerInvariant()}";

        /// <summary>Device root without a trailing slash, /api or /api/v1 (copied-from-AWTRIX-3 addresses).</summary>
        public static string HttpRoot(string baseTopic)
        {
            var root = baseTopic.TrimEnd('/');
            foreach (var suffix in new[] { "/api/v1", "/api" })
            {
                if (root.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    root = root.Substring(0, root.Length - suffix.Length).TrimEnd('/');
                    break;
                }
            }
            return root;
        }
    }
}
```

`AwtrixFirmware.cs`: add `private static readonly IAwtrixFirmware Ng = new NgFirmware();` and the case `AwtrixFirmwareKind.NG => Ng,`.

- [ ] **Step 4: Run tests** — `dotnet test test/Test/Test.csproj` → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/api/Services/Firmware test/Test/Services/Firmware
git commit -m "feat(ng): NgFirmware addressing for MQTT and HTTP API v1"
```

---

### Task 7: Service warnings for dropped keys; `ButtonApp` topics via firmware

**Files:**
- Modify: `src/api/Services/AwtrixService.cs`, `src/api/Apps/Buttons/ButtonApp.cs`
- Test: `test/Test/Services/AwtrixServicePublishTests.cs` (append), `test/Test/Apps/Buttons/ButtonAppTests.cs` (append)

**Interfaces:**
- Consumes: `AwtrixRequest.DroppedKeys`, `IAwtrixFirmware.ButtonTopic`.

- [ ] **Step 1: Write the failing tests**

Append to `AwtrixServicePublishTests` (add `using AwtrixSharpWeb.Services.Firmware;`, `using Microsoft.Extensions.Logging;`, `using Moq;`):
```csharp
        [Fact]
        public async Task AppUpdate_NgDevice_UsesNgTopicAndPayload()
        {
            var (service, _, mqtt) = CreateService();
            var address = new AwtrixAddress { BaseTopic = "awtrix/clock2", Firmware = AwtrixFirmwareKind.NG };

            await service.AppUpdate(address, "X", new AwtrixAppMessage().SetText("42").SetColor("FF0000"));

            Assert.Equal("awtrix/clock2/cmd/apps/pushed/X", mqtt.LastUrl);
            Assert.Equal("{\"text\":\"42\",\"textColor\":\"#FF0000\"}", mqtt.LastPayload);
        }

        [Fact]
        public async Task AppClear_NgHttpDevice_SendsDelete()
        {
            var (service, http, _) = CreateService();
            var address = new AwtrixAddress { BaseTopic = "http://localhost:8080", Firmware = AwtrixFirmwareKind.NG };

            await service.AppClear(address, "X");

            Assert.Equal("http://localhost:8080/api/v1/apps/X", http.LastUrl);
            Assert.Equal(HttpMethod.Delete, http.LastMethod);
        }

        [Fact]
        public async Task AppUpdate_NgDroppedKey_LogsWarningOncePerDeviceAndKey()
        {
            var logger = new Mock<ILogger<AwtrixService>>();
            logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
            var service = new AwtrixService(new FakeHttpPublisher(), new FakeMqttPublisher(), logger.Object);
            var clock2 = new AwtrixAddress { BaseTopic = "awtrix/clock2", Firmware = AwtrixFirmwareKind.NG };
            var clock3 = new AwtrixAddress { BaseTopic = "awtrix/clock3", Firmware = AwtrixFirmwareKind.NG };
            var message = new AwtrixAppMessage().SetText("x").SetTopText(true);

            await service.AppUpdate(clock2, "X", message);
            await service.AppUpdate(clock2, "X", message);
            await service.AppUpdate(clock3, "X", message);

            logger.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("topText") && v.ToString()!.Contains("awtrix/clock2")),
                null, It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
            logger.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("topText") && v.ToString()!.Contains("awtrix/clock3")),
                null, It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }
```

Append to `ButtonAppTests`:
```csharp
        [Fact]
        public async Task InitAsync_NgDevice_SubscribesToNgButtonTopics()
        {
            var sut = CreateSut();
            _address.Firmware = AwtrixSharpWeb.Services.Firmware.AwtrixFirmwareKind.NG;
            // CreateSut built the topics from _address at construction time, so build a fresh app with the NG address
            var ngAddress = new AwtrixAddress { BaseTopic = "test/base/topic", Firmware = AwtrixSharpWeb.Services.Firmware.AwtrixFirmwareKind.NG };
            var ng = new ButtonApp(_mockLogger.Object, new AppConfig(), ngAddress, _mockAwtrixService.Object, _mockMqttConnector.Object);

            await ng.InitAsync();

            _mockMqttConnector.Verify(x => x.Subscribe("test/base/topic/state/buttons/left"), Times.Once);
            _mockMqttConnector.Verify(x => x.Subscribe("test/base/topic/state/buttons/right"), Times.Once);
            _mockMqttConnector.Verify(x => x.Subscribe("test/base/topic/state/buttons/select"), Times.Once);
            _mockMqttConnector.Verify(x => x.Subscribe(It.Is<string>(t => t.Contains("stats/button"))), Times.Never);
        }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~AwtrixServicePublishTests|FullyQualifiedName~ButtonAppTests"`
Expected: the NG tests fail (no warning logged; wrong button topics). The first two NG service tests may already pass from Task 6; that is fine.

- [ ] **Step 3: Implement**

`AwtrixService.cs`: add `using System.Collections.Concurrent;` and a field
```csharp
        /// <summary>(BaseTopic, key) pairs already warned about: a misconfigured ValueMap is visible once, not every tick.</summary>
        private readonly ConcurrentDictionary<(string, string), byte> _warnedDroppedKeys = new();
```
In `SafePublish`, after `var request = build(firmware);`:
```csharp
                foreach (var key in request.DroppedKeys)
                {
                    if (_warnedDroppedKeys.TryAdd((baseTopic, key), 0))
                    {
                        _logger.LogWarning("{Firmware} has no equivalent for '{Key}' (or its value is invalid); it was dropped for {BaseTopic}", firmware.Kind, key, baseTopic);
                    }
                    else
                    {
                        _logger.LogDebug("Dropped '{Key}' again for {BaseTopic}", key, baseTopic);
                    }
                }
```

`ButtonApp.cs`: add `using AwtrixSharpWeb.Services.Firmware;` and replace `GetTopic`:
```csharp
        private string GetTopic(Button button)
        {
            return AwtrixFirmware.For(AwtrixAddress.Firmware).ButtonTopic(AwtrixAddress, button);
        }
```

- [ ] **Step 4: Run all tests** — `dotnet test test/Test/Test.csproj` → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/api/Services/AwtrixService.cs src/api/Apps/Buttons/ButtonApp.cs test/Test/Services/AwtrixServicePublishTests.cs test/Test/Apps/Buttons/ButtonAppTests.cs
git commit -m "feat(ng): warn once per dropped key; ButtonApp subscribes via the firmware profile"
```

---

### Task 8: Simulator environment

**Files:**
- Create: `src/api/appsettings.Simulator.json`, `docs/simulator.md`
- Test: `test/Test/Configuration/SimulatorEnvironmentTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.UserSecrets;

namespace Test.Configuration
{
    /// <summary>
    /// The Simulator environment is the only one the app may be run in locally: it must not load user secrets
    /// (which hold the real broker and clock) and must target only the NG simulator on localhost.
    /// </summary>
    public class SimulatorEnvironmentTests : IDisposable
    {
        private readonly List<WebApplicationBuilder> _builders = new();
        private static readonly string ApiProjectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "api"));

        private WebApplicationBuilder CreateBuilder(string environment)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = ApiProjectDir,
                EnvironmentName = environment,
                ApplicationName = "awtrix-api",
            });
            AwtrixSharpWeb.Program.SetupConfiguration(builder.Configuration, builder.Services);
            _builders.Add(builder);
            return builder;
        }

        [Fact]
        public void Simulator_DoesNotLoadUserSecrets()
        {
            var builder = CreateBuilder("Simulator");

            var sources = ((IConfigurationBuilder)builder.Configuration).Sources;
            Assert.DoesNotContain(sources, s => s is JsonConfigurationSource json && json.Path != null && json.Path.EndsWith("secrets.json", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Simulator_LoadsSimulatorSettingsFile()
        {
            var builder = CreateBuilder("Simulator");

            var sources = ((IConfigurationBuilder)builder.Configuration).Sources.OfType<JsonConfigurationSource>().Select(s => s.Path).ToList();
            Assert.Contains("appsettings.Simulator.json", sources);
        }

        [Fact]
        public void Simulator_TargetsOnlyTheLocalSimulator()
        {
            var builder = CreateBuilder("Simulator");
            var config = builder.Configuration.GetSection("Awtrix").Get<AwtrixSharpWeb.Domain.AwtrixConfig>()!;

            var device = Assert.Single(config.Devices);
            Assert.Equal("http://localhost:8080", device.BaseTopic);
            Assert.Equal(AwtrixSharpWeb.Services.Firmware.AwtrixFirmwareKind.NG, device.Firmware);
            Assert.Equal("localhost", builder.Configuration["Mqtt:Host"]);
        }

        public void Dispose()
        {
            foreach (var builder in _builders)
            {
                try { builder.Build().DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
            }
        }
    }
}
```
If `Microsoft.Extensions.Configuration.UserSecrets` is not referenced by the test project, delete that `using`; the assertion only needs `JsonConfigurationSource`. Check how `ConfigurationPrecedenceTests.Dispose` disposes builders and copy that exactly instead of the `try/catch` above. If `ApiProjectDir` does not resolve (test bin depth differs), compute it by walking up from `AppContext.BaseDirectory` until a directory containing `awtrix-api.csproj` is found under `src/api`.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~SimulatorEnvironmentTests"`
Expected: `Simulator_LoadsSimulatorSettingsFile` fails (file missing) and `Simulator_TargetsOnlyTheLocalSimulator` fails (`Devices` comes from `appsettings.json`: `awtrix/clock1`). `Simulator_DoesNotLoadUserSecrets` should already pass (ASP.NET loads secrets only in Development); keep it as the guard.

- [ ] **Step 3: Implement**

`src/api/appsettings.Simulator.json`:
```json
{
  "Mqtt": {
    "Host": "localhost",
    "Username": "",
    "Password": ""
  },
  "Awtrix": {
    "Devices": [
      {
        "BaseTopic": "http://localhost:8080",
        "Firmware": "NG",
        "Apps": [
          {
            "Type": "DiurnalApp",
            "Config": {
              "0600": "Brightness=8",
              "0700": "GlobalTextColor=#FFFFFF",
              "1900": "GlobalTextColor=#FF0000",
              "2100": "Brightness=1"
            }
          },
          {
            "Type": "MqttClockRenderApp",
            "Config": {
              "CronSchedule": "0 0 * * *",
              "ActiveTime": "23:59:00",
              "ReadTopic": "awtrix-sim/temperature"
            }
          }
        ]
      }
    ]
  },
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```
Check the csproj copies `appsettings.*.json` to output (default SDK behaviour for `appsettings.Development.json` is via `Content` glob; verify with `dotnet build` and `ls src/api/bin/Debug/net10.0/appsettings.Simulator.json`). The configuration binder overrides arrays by index, so `Devices:0` replaces clock1 and there is only one device in `appsettings.json`, which is why `Assert.Single` holds; if `appsettings.json` later gains a second device this test will fail deliberately.

`docs/simulator.md`:
```markdown
# Running against the AWTRIX NG simulator

The NG simulator runs the real firmware and web UI on your PC and serves HTTP API v1 on
`http://localhost:8080`. It is the only target AwtrixSharp may be run against locally.

## Build the simulator (once)

Needs PlatformIO (`pip install platformio`).

```bash
git clone https://github.com/Blueforcer/awtrix-ng
cd awtrix-ng
pio run -e native_sim
.pio\build\native_sim\program.exe      # Windows; .pio/build/native_sim/program on Linux/macOS
```

Open http://localhost:8080 for the live preview grid. `--port N` changes the port; `--no-matrix` silences the
terminal rendering.

## Run AwtrixSharp against it

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Simulator"
dotnet run --project src/api
```

`appsettings.Simulator.json` lists one NG device at `http://localhost:8080` and points MQTT at `localhost`.
User secrets are only loaded in the Development environment, so the real broker and clock are unreachable
(`test/Test/Configuration/SimulatorEnvironmentTests.cs` pins this).

## Optional: MQTT path

Run a local broker (`docker run -p 1883:1883 eclipse-mosquitto`), then in the simulator web UI (or
`PUT /api/v1/system`) set `mqttEnabled: true`, `mqttHost: <your PC's IP>`, `mqttPrefix: awtrix/sim`. Change the
device in `appsettings.Simulator.json` to `"BaseTopic": "awtrix/sim"` and publish a test value:
`mosquitto_pub -t awtrix-sim/temperature -m 21.5`.

## What to check

- Preview grid shows the pushed app; `GET http://localhost:8080/api/v1/apps` lists it.
- Diurnal: `GET /api/v1/settings` reflects `brightness`/`textColor` after the scheduled time.
- Any `422` is logged by `HttpPublisher` with the body naming the rejected field.
- Buttons: `POST /sim/...` endpoints (see the simulator docs) publish `state/buttons/*` over MQTT.
```

- [ ] **Step 4: Run tests** — `dotnet test test/Test/Test.csproj` → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/api/appsettings.Simulator.json docs/simulator.md test/Test/Configuration/SimulatorEnvironmentTests.cs
git commit -m "feat: Simulator environment targeting the AWTRIX NG simulator only"
```

---

### Task 9: Documentation

**Files:**
- Modify: `README.md` (new section before `## Environment Variables`), `CLAUDE.md` (Architecture section)

- [ ] **Step 1: README section**

Insert before `## Environment Variables`:
```markdown
## AWTRIX NG

AwtrixSharp drives both [AWTRIX 3](https://blueforcer.github.io/awtrix3) and
[AWTRIX NG](https://blueforcer.github.io/awtrix-ng/) clocks. Add `"Firmware": "NG"` to a device; the default is
`Awtrix3`, so existing configs are unchanged.

```json
{ "BaseTopic": "awtrix/clock2", "Firmware": "NG", "Apps": [ ... ] }
```

`BaseTopic` for NG is the device's `mqttPrefix` (MQTT) or its root URL such as `http://192.168.1.51` (HTTP).

App config keeps the AWTRIX 3 vocabulary (`Color`, `Duration` in seconds, `PushIcon` 0/1/2, `Bar`, `ProgressC`,
Diurnal `Brightness=`/`GlobalTextColor=`); the service translates it for NG. Keys with no NG equivalent
(`TopText`) are dropped with one warning per device. NG-only features (multiple icons, palette animation,
scripts) are not exposed. NG effect names differ from AWTRIX 3 and are passed through unchanged; an unknown
name is rejected by the clock with a 422 that appears in the log.

To test against the NG simulator without a clock, see [docs/simulator.md](docs/simulator.md).
```

- [ ] **Step 2: CLAUDE.md**

Under `### Two Publisher Transports`, replace the two bullets and the `AwtrixAddress` sentence with:
```markdown
- **MQTT** (`MqttPublisher`) and **HTTP** (`HttpPublisher`) are dumb transports: they send an `AwtrixRequest(Address, Method, Payload)`.
- `AwtrixAddress.BaseTopic` (starts with `http` or not) selects the transport; `AwtrixAddress.Firmware` (`Awtrix3` default, `NG`) selects the dialect via `AwtrixFirmware.For(...)` in `src/api/Services/Firmware/`. `Awtrix3Firmware` is transitional and deletable; `NgFirmware` + `NgPayloadTranslator` map the AWTRIX 3 config vocabulary onto NG topics/endpoints/keys. See `docs/superpowers/specs/2026-09-24-awtrix-ng-firmware-design.md`.
- Never run the app in the Development environment (user secrets point at the real broker/clock). `ASPNETCORE_ENVIRONMENT=Simulator` targets only the NG simulator (`docs/simulator.md`).
```

- [ ] **Step 3: Build and run all tests** — `dotnet build && dotnet test test/Test/Test.csproj` → PASS.

- [ ] **Step 4: Commit**

```bash
git add README.md CLAUDE.md
git commit -m "docs: AWTRIX NG firmware setting and simulator runbook"
```

---

## Self-review notes

- Spec coverage: config (T1), request/publishers/4xx logging (T2), profile seam + AWTRIX 3 pin (T3), payload table + rules (T4), settings (T5), NG addressing incl. HTTP root normalisation (T6), dropped-key warning + buttons (T7), simulator env + test + runbook (T8), docs (T9). "Deleting AWTRIX 3 later" needs no task.
- Type consistency: `AwtrixRequest(Address, Method, Payload, DroppedKeys)`, `NgTranslation(Json, DroppedKeys)`, `NgPayloadKind`, `AwtrixFirmware.For`, `NgFirmware.HttpRoot` are named identically across tasks.
- Known implementation nit flagged inline in Task 4 (`fragment["color"]` lookup).
