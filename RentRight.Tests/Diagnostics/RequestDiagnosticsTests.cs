using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using RentRight.Diagnostics;
using RentRight.Integrations.Geocoding;
using RentRight.Tests.Api;
using Xunit;

namespace RentRight.Tests.Diagnostics;

public sealed class RequestDiagnosticsTests
{
    private const string Probe = "/api/v1/diagnostics-probe";
    private const string Parent = "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01";

    [Fact]
    public async Task Request_UntrustedValues_LogsOnlyRouteTemplateAndSafeFields()
    {
        using var logs = new CapturedLogs();
        await using var factory = Factory(logs);
        using var client = Client(factory);
        using var request = new HttpRequestMessage(HttpMethod.Get, Probe + "/items/private-path-marker?password=private-query-marker");
        request.Headers.Add("Cookie", "ignored=private-cookie-marker");
        request.Headers.Add("Authorization", "Bearer private-authorization-marker");
        request.Headers.Add("X-CSRF-TOKEN", "private-csrf-marker");
        request.Headers.Add("X-Correlation-ID", "private-correlation-marker");
        request.Headers.Add("baggage", "email=private-baggage-marker");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = Assert.Single(logs.Entries, value => value.Event.Id == 13001);
        Assert.Equal("api/v1/diagnostics-probe/items/{id}", entry.Fields["Route"]);
        Assert.Equal("GET", entry.Fields["Method"]);
        Assert.Equal(200, entry.Fields["StatusCode"]);
        Assert.True(Assert.IsType<double>(entry.Fields["ElapsedMilliseconds"]) >= 0);
        Assert.Matches("^[a-f0-9]{32}$", Assert.IsType<string>(entry.Fields["TraceId"]));
        Assert.Equal(new[] { "ElapsedMilliseconds", "Method", "Outcome", "Route", "SpanId", "StatusCode", "TraceId", "{OriginalFormat}" }, entry.Fields.Keys.Order(StringComparer.Ordinal));
        AssertPrivateValuesAbsent(logs, "private-path-marker", "private-query-marker", "private-cookie-marker", "private-authorization-marker",
            "private-csrf-marker", "private-correlation-marker", "private-baggage-marker");
    }

    [Fact]
    public async Task Login_InvalidAntiforgery_StaysProtectedWithoutLoggingFormCredentials()
    {
        using var logs = new CapturedLogs();
        await using var factory = Factory(logs);
        using var client = Client(factory);
        using var response = await client.PostAsync("/Identity/Account/Login?returnUrl=private-return-marker", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["Input.Email"] = "private-email-marker@example.test", ["Input.Password"] = "private-password-marker",
                ["__RequestVerificationToken"] = "private-form-token-marker"
            }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var summary = Assert.Single(logs.Entries, value => value.Event.Id == 13001);
        Assert.Equal("POST", summary.Fields["Method"]);
        Assert.Contains("Account/Login", (string)summary.Fields["Route"]!);
        AssertPrivateValuesAbsent(logs, "private-email-marker", "private-password-marker", "private-form-token-marker", "private-return-marker");
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("application/xml")]
    public async Task Failure_ApiWithUnsupportedAccept_StillWritesSafeProblemJson(string accept)
    {
        using var logs = new CapturedLogs();
        await using var factory = Factory(logs);
        using var client = Client(factory);
        client.DefaultRequestHeaders.Accept.ParseAdd(accept);
        using var response = await client.GetAsync("/api/v1/api-convention-probe/exception");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("internal_error", document.RootElement.GetProperty("code").GetString());
        AssertPrivateValuesAbsent(logs, "SQL provider detail", "credential detail", "internal frame");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Failure_Api_LogsOneSafeFaultAndCorrelatesExistingProblemContract(string environment)
    {
        using var logs = new CapturedLogs();
        await using var factory = Factory(logs, environment: environment);
        using var client = Client(factory);
        client.DefaultRequestHeaders.Add("traceparent", Parent);
        using var response = await client.GetAsync("/api/v1/api-convention-probe/exception");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("internal_error", problem.RootElement.GetProperty("code").GetString());
        var summary = Assert.Single(logs.Entries, value => value.Event.Id == 13001);
        var fault = Assert.Single(logs.Entries, value => value.Event.Id == 13002);
        Assert.Equal(500, summary.Fields["StatusCode"]);
        Assert.Equal("failed", summary.Fields["Outcome"]);
        Assert.Equal("0123456789abcdef0123456789abcdef", summary.Fields["TraceId"]);
        Assert.Equal(summary.Fields["TraceId"], fault.Fields["TraceId"]);
        Assert.Contains((string)summary.Fields["TraceId"]!, problem.RootElement.GetProperty("traceId").GetString());
        AssertPrivateValuesAbsent(logs, "SQL provider detail", "credential detail", "internal frame");
        Assert.All(logs.Entries.Where(value => value.Category.StartsWith("RentRight.Diagnostics")), value => Assert.Null(value.Exception));
    }

    [Fact]
    public async Task Request_InvalidTraceParent_GeneratesNewW3cContextWithoutEchoingInput()
    {
        using var logs = new CapturedLogs();
        await using var factory = Factory(logs);
        using var client = Client(factory);
        client.DefaultRequestHeaders.Add("traceparent", "private-invalid-parent-marker");
        using var response = await client.GetAsync(Probe + "/items/7");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = Assert.Single(logs.Entries, value => value.Event.Id == 13001);
        Assert.Matches("^[a-f0-9]{32}$", (string)summary.Fields["TraceId"]!);
        AssertPrivateValuesAbsent(logs, "private-invalid-parent-marker");
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task Failure_Mvc_PreservesSafeHtmlAndOriginalRoute(string method)
    {
        using var logs = new CapturedLogs();
        await using var factory = Factory(logs);
        using var client = Client(factory);
        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), "/diagnostics-probe/mvc"));
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Request ID", body);
        Assert.DoesNotContain("private-mvc-exception-marker", body);
        var summary = Assert.Single(logs.Entries, value => value.Event.Id == 13001);
        Assert.Equal("diagnostics-probe/mvc", summary.Fields["Route"]);
        Assert.Single(logs.Entries, value => value.Event.Id == 13002);
        AssertPrivateValuesAbsent(logs, "private-mvc-exception-marker");
    }

    [Theory]
    [InlineData("/api/v1/api-convention-probe/secure", 401)]
    [InlineData("/api/v1/api-convention-probe/outcomes/Forbidden", 403)]
    [InlineData("/api/v1/api-convention-probe/outcomes/Conflict", 409)]
    [InlineData("/api/v1/private-unknown-route", 404)]
    public async Task Request_Failures_AreLoggedEvenWhenSuccessSamplingIsZero(string path, int status)
    {
        using var logs = new CapturedLogs();
        await using var factory = Factory(logs, options => options.SuccessfulRequestLogSampleRate = 0);
        using var client = Client(factory);
        using var response = await client.GetAsync(path);
        Assert.Equal(status, (int)response.StatusCode);
        var summary = Assert.Single(logs.Entries, value => value.Event.Id == 13001);
        Assert.Equal(status, summary.Fields["StatusCode"]);
        if (status == 404) Assert.Equal("unmatched", summary.Fields["Route"]);
        Assert.DoesNotContain(logs.Entries, value => value.Event.Id == 13002);
    }

    [Fact]
    public async Task Request_Body_IsNotCapturedAndZeroSamplingDoesNotDisableMetrics()
    {
        using var logs = new CapturedLogs();
        await using var factory = Factory(logs, options => options.SuccessfulRequestLogSampleRate = 0);
        using var client = Client(factory);
        var measurements = new ConcurrentQueue<(string Name, object Value, KeyValuePair<string, object?>[] Tags)>();
        using var listener = Metrics(factory, measurements);
        using var response = await client.PostAsJsonAsync(Probe + "/echo", new { description = "private-tenant-content-marker" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(logs.Entries, value => value.Event.Id == 13001);
        AssertPrivateValuesAbsent(logs, "private-tenant-content-marker");
        Assert.Contains(measurements, value => value.Name == "rentright.http.requests" && (long)value.Value == 1);
        Assert.Contains(measurements, value => value.Name == "rentright.http.duration" && (double)value.Value >= 0);
        Assert.All(measurements, value => Assert.Equal(new[] { "http.request.method", "http.response.status_code", "http.route", "outcome" }, value.Tags.Select(tag => tag.Key).Order()));
    }

    [Fact]
    public async Task Diagnostics_Disabled_RetainsHttpBehaviorWithoutRequestLogsOrCustomMeasurements()
    {
        using var logs = new CapturedLogs();
        await using var factory = Factory(logs, options => options.Enabled = false);
        using var client = Client(factory);
        var measurements = new ConcurrentQueue<(string Name, object Value, KeyValuePair<string, object?>[] Tags)>();
        using var listener = Metrics(factory, measurements);
        using var response = await client.GetAsync(Probe + "/items/7");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(logs.Entries, value => value.Event.Id == 13001);
        Assert.Empty(measurements);
    }

    [Fact]
    public async Task Diagnostics_Disabled_StillRecordsSafeUnexpectedFailure()
    {
        using var logs = new CapturedLogs();
        await using var factory = Factory(logs, options => options.Enabled = false);
        using var client = Client(factory);
        using var response = await client.GetAsync("/api/v1/api-convention-probe/exception");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Single(logs.Entries, value => value.Event.Id == 13002);
        Assert.DoesNotContain(logs.Entries, value => value.Event.Id == 13001);
        AssertPrivateValuesAbsent(logs, "SQL provider detail", "credential detail", "internal frame");
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task Diagnostics_InvalidSampling_FailsStartup(double rate)
    {
        using var logs = new CapturedLogs();
        await using var factory = Factory(logs, options => options.SuccessfulRequestLogSampleRate = rate);
        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    [Fact]
    public async Task Console_Defaults_AreJsonUtcWithoutArbitraryScopes()
    {
        using var logs = new CapturedLogs();
        await using var factory = Factory(logs);
        using var client = Client(factory);
        Assert.Equal(ConsoleFormatterNames.Json, factory.Services.GetRequiredService<IOptions<ConsoleLoggerOptions>>().Value.FormatterName);
        var options = factory.Services.GetRequiredService<IOptionsMonitor<JsonConsoleFormatterOptions>>().CurrentValue;
        Assert.True(options.UseUtcTimestamp);
        Assert.False(options.IncludeScopes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Geocoding_FakeTransport_SharesRequestTraceWithoutCapturingAddressKeyOrUri(bool disabled)
    {
        using var logs = new CapturedLogs();
        var transport = new FakeGeoTransport();
        await using var factory = Factory(logs, options => options.Enabled = !disabled, configure: services =>
        {
            services.Configure<GeocodingOptions>(options => { options.Enabled = true; options.ApiKey = "private-provider-key-marker"; });
            services.AddHttpClient<IGeocodingClient, GeoapifyGeocodingClient>().ConfigurePrimaryHttpMessageHandler(() => transport).RemoveAllLoggers();
        });
        using var client = Client(factory);
        var spans = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == RentalDiagnostics.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Enqueue
        };
        ActivitySource.AddActivityListener(listener);
        client.DefaultRequestHeaders.Add("traceparent", Parent);
        using var response = await client.GetAsync(Probe + "/lookup");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, transport.Calls);
        var own = spans.Where(span => span.TraceId.ToHexString() == "0123456789abcdef0123456789abcdef").ToArray();
        if (disabled) Assert.Empty(own);
        else
        {
            var span = Assert.Single(own);
            Assert.Equal("geocoding.lookup", span.OperationName);
            Assert.Equal("NoResult", span.GetTagItem("outcome"));
            var summary = Assert.Single(logs.Entries, value => value.Event.Id == 13001);
            Assert.Equal(summary.Fields["TraceId"], span.TraceId.ToHexString());
            Assert.Equal(summary.Fields["SpanId"], span.ParentSpanId.ToHexString());
            Assert.Equal(new[] { "outcome" }, span.TagObjects.Select(tag => tag.Key));
        }
        AssertPrivateValuesAbsent(logs, "private-address-marker", "private-provider-key-marker", "api.geoapify.com");
    }

    [Fact]
    public async Task Middleware_CancelledOrUnhandledRequests_PreserveExceptionsAndRecordSafeOutcome()
    {
        using var logs = new CapturedLogs();
        using var services = new ServiceCollection().AddRentalDiagnostics(new ConfigurationBuilder().Build())
            .AddLogging(logging => logging.AddProvider(logs)).BuildServiceProvider();
        var diagnostics = services.GetRequiredService<RentalDiagnostics>();
        var logger = services.GetRequiredService<ILogger<RequestDiagnosticsMiddleware>>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        context.Request.Method = "PRIVATE-VERB";
        context.Request.Path = "/private-raw-path-marker";
        var middleware = new RequestDiagnosticsMiddleware(_ => throw new OperationCanceledException(cancellation.Token), logger);
        await Assert.ThrowsAsync<OperationCanceledException>(() => middleware.InvokeAsync(context, diagnostics));
        Assert.Equal(200, context.Response.StatusCode);
        var cancelled = Assert.Single(logs.Entries, value => value.Event.Id == 13001);
        Assert.Equal("cancelled", cancelled.Fields["Outcome"]);
        Assert.Equal("OTHER", cancelled.Fields["Method"]);
        Assert.Equal(499, cancelled.Fields["StatusCode"]);
        Assert.DoesNotContain(logs.Entries, value => value.Event.Id == 13002);
        var failure = new InvalidOperationException("private-unhandled-marker");
        var failing = new RequestDiagnosticsMiddleware(_ => throw failure, logger);
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => failing.InvokeAsync(new DefaultHttpContext(), diagnostics)));
        Assert.Single(logs.Entries, value => value.Event.Id == 13002);
        AssertPrivateValuesAbsent(logs, "private-raw-path-marker", "PRIVATE-VERB", "private-unhandled-marker");
    }

    [Fact]
    public async Task Middleware_ResponseAlreadyStarted_RecordsAbortWithoutRewritingStatusOrLoggingException()
    {
        using var logs = new CapturedLogs();
        using var services = new ServiceCollection().AddRentalDiagnostics(new ConfigurationBuilder().Build())
            .AddLogging(logging => logging.AddProvider(logs)).BuildServiceProvider();
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(new StartedResponse());
        var failure = new InvalidOperationException("private-stream-exception-marker");
        var middleware = new RequestDiagnosticsMiddleware(_ => throw failure, services.GetRequiredService<ILogger<RequestDiagnosticsMiddleware>>());
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context, services.GetRequiredService<RentalDiagnostics>())));
        Assert.Equal(200, context.Response.StatusCode);
        var summary = Assert.Single(logs.Entries, value => value.Event.Id == 13001);
        Assert.Equal("aborted", summary.Fields["Outcome"]);
        Assert.Equal(200, summary.Fields["StatusCode"]);
        AssertPrivateValuesAbsent(logs, "private-stream-exception-marker");
    }

    [Fact]
    public async Task Middleware_LegacyActivity_CreatesW3cContextAndRestoresAmbientParent()
    {
        using var logs = new CapturedLogs();
        using var services = new ServiceCollection().AddRentalDiagnostics(new ConfigurationBuilder().Build())
            .AddLogging(logging => logging.AddProvider(logs)).BuildServiceProvider();
        using var legacy = new Activity("Legacy test").SetIdFormat(ActivityIdFormat.Hierarchical).SetParentId("|private-legacy-parent-marker.").Start();
        var middleware = new RequestDiagnosticsMiddleware(_ =>
        {
            Assert.Equal(ActivityIdFormat.W3C, Activity.Current?.IdFormat);
            Assert.NotEqual(default, Activity.Current!.TraceId);
            return Task.CompletedTask;
        }, services.GetRequiredService<ILogger<RequestDiagnosticsMiddleware>>());
        await middleware.InvokeAsync(new DefaultHttpContext(), services.GetRequiredService<RentalDiagnostics>());
        Assert.Same(legacy, Activity.Current);
        AssertPrivateValuesAbsent(logs, "private-legacy-parent-marker");
    }

    private sealed class StartedResponse : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }

    private static RentRightApplicationFactory Factory(CapturedLogs logs, Action<DiagnosticsOptions>? options = null,
        string environment = "Production", Action<IServiceCollection>? configure = null) => new(environment, services =>
    {
        services.AddLogging(logging => logging.AddProvider(logs));
        services.Configure<DiagnosticsOptions>(value => { value.Enabled = true; value.SuccessfulRequestLogSampleRate = 1; options?.Invoke(value); });
        configure?.Invoke(services);
    });

    private static HttpClient Client(RentRightApplicationFactory factory) => factory.CreateClient(new WebApplicationFactoryClientOptions
    { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });

    private static MeterListener Metrics(RentRightApplicationFactory factory, ConcurrentQueue<(string Name, object Value, KeyValuePair<string, object?>[] Tags)> measurements)
    {
        var factoryScope = factory.Services.GetRequiredService<IMeterFactory>();
        var listener = new MeterListener { InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Meter.Name == RentalDiagnostics.Name && ReferenceEquals(instrument.Meter.Scope, factoryScope)) current.EnableMeasurementEvents(instrument);
        } };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => measurements.Enqueue((instrument.Name, value, tags.ToArray())));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => measurements.Enqueue((instrument.Name, value, tags.ToArray())));
        listener.Start();
        return listener;
    }

    private static void AssertPrivateValuesAbsent(CapturedLogs logs, params string[] secrets)
    {
        foreach (var entry in logs.Entries)
        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(secret, entry.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(secret, string.Join(' ', entry.Fields.Values), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(secret, entry.Exception?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class FakeGeoTransport : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"results\":[]}") });
        }
    }
}
