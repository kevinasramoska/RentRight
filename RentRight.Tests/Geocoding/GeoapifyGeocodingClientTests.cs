using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RentRight.Integrations.Geocoding;
using Xunit;

namespace RentRight.Tests.Geocoding;

public sealed class GeoapifyGeocodingClientTests
{
    private const string Valid = """{"results":[{"lat":53.349805,"lon":-6.26031,"place_id":"synthetic-place","result_type":"building","rank":{"confidence":0.95},"datasource":{"sourcename":"openstreetmap"}}]}""";

    [Fact]
    public async Task Lookup_Disabled_DoesNotSendRequestOrRequireKey()
    {
        using var fixture = new Fixture(_ => Json(Valid), enabled: false);
        Assert.Equal(GeocodingStatus.Disabled, (await fixture.Client.LookupAsync("1 Synthetic Street", default)).Status);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Fact]
    public async Task Lookup_ValidMatch_UsesFixedEscapedEndpointAndCachesNormalizedAddress()
    {
        using var fixture = new Fixture(_ => Json(Valid));
        var first = await fixture.Client.LookupAsync("  1   Synthetic Street & apiKey=forged  ", default);
        var second = await fixture.Client.LookupAsync("1 synthetic street & apikey=forged", default);
        Assert.Equal(GeocodingStatus.Found, first.Status);
        Assert.Equal(53.349805m, first.Location!.Latitude);
        Assert.Equal(-6.260310m, first.Location.Longitude);
        Assert.Equal(first, second);
        var uri = Assert.Single(fixture.Handler.Requests);
        Assert.Equal("https", uri.Scheme);
        Assert.Equal("api.geoapify.com", uri.Host);
        Assert.Equal("/v1/geocode/search", uri.AbsolutePath);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        Assert.Equal("1 Synthetic Street & apiKey=forged", query["text"]);
        Assert.Equal("test-only-key", query["apiKey"]);
        Assert.Equal("1", query["limit"]);
    }

    [Fact]
    public async Task Lookup_NoResult_IsNegativelyCachedAndExpiresAfterFiveMinutes()
    {
        var time = new TestClock();
        using var fixture = new Fixture(_ => Json("{\"results\":[]}"), clock: time);
        Assert.Equal(GeocodingStatus.NoResult, (await fixture.Client.LookupAsync("Synthetic", default)).Status);
        await fixture.Client.LookupAsync("Synthetic", default);
        Assert.Single(fixture.Handler.Requests);
        time.Advance(TimeSpan.FromMinutes(6));
        await fixture.Client.LookupAsync("Synthetic", default);
        Assert.Equal(2, fixture.Handler.Requests.Count);
    }

    [Fact]
    public async Task Lookup_PositiveCache_ExpiresAfterTwentyFourHours()
    {
        var time = new TestClock();
        using var fixture = new Fixture(_ => Json(Valid), clock: time);
        await fixture.Client.LookupAsync("Synthetic", default);
        time.Advance(TimeSpan.FromHours(23));
        await fixture.Client.LookupAsync("Synthetic", default);
        Assert.Single(fixture.Handler.Requests);
        time.Advance(TimeSpan.FromHours(2));
        await fixture.Client.LookupAsync("Synthetic", default);
        Assert.Equal(2, fixture.Handler.Requests.Count);
    }

    [Theory]
    [InlineData("network")]
    [InlineData("503")]
    public async Task Lookup_TransientFailure_RetriesAtMostOnceAndThenReturnsMatch(string failure)
    {
        var attempts = 0;
        using var fixture = new Fixture(_ => ++attempts == 1
            ? failure == "network" ? throw new HttpRequestException("private address and API key") : new(HttpStatusCode.ServiceUnavailable)
            : Json(Valid));
        Assert.Equal(GeocodingStatus.Found, (await fixture.Client.LookupAsync("Synthetic", default)).Status);
        Assert.Equal(2, fixture.Handler.Requests.Count);
    }

    [Fact]
    public async Task Lookup_RepeatedServerFailure_IsBoundedToTwoAttempts()
    {
        using var fixture = new Fixture(_ => new(HttpStatusCode.BadGateway));
        Assert.Equal(GeocodingStatus.Unavailable, (await fixture.Client.LookupAsync("Synthetic", default)).Status);
        Assert.Equal(2, fixture.Handler.Requests.Count);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(302)]
    public async Task Lookup_PermanentFailureOrRedirect_IsNotRetried(int status)
    {
        using var fixture = new Fixture(_ => new((HttpStatusCode)status));
        Assert.Equal(GeocodingStatus.Unavailable, (await fixture.Client.LookupAsync("Synthetic", default)).Status);
        Assert.Single(fixture.Handler.Requests);
    }

    [Theory]
    [InlineData(429, false)]
    [InlineData(429, true)]
    [InlineData(503, false)]
    public async Task Lookup_RetryAfter_HonorsSharedCooldownWithoutImmediateRetry(int status, bool dateHeader)
    {
        var time = new TestClock();
        var attempts = 0;
        using var fixture = new Fixture(_ =>
        {
            if (++attempts > 1) return Json(Valid);
            var response = new HttpResponseMessage((HttpStatusCode)status);
            response.Headers.RetryAfter = dateHeader ? new RetryConditionHeaderValue(time.GetUtcNow().AddMinutes(2))
                : new RetryConditionHeaderValue(TimeSpan.FromMinutes(2));
            return response;
        }, clock: time);
        Assert.Equal(GeocodingStatus.RateLimited, (await fixture.Client.LookupAsync("First", default)).Status);
        time.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(GeocodingStatus.RateLimited, (await fixture.Client.LookupAsync("Second", default)).Status);
        Assert.Single(fixture.Handler.Requests);
        time.Advance(TimeSpan.FromSeconds(60));
        Assert.Equal(GeocodingStatus.Found, (await fixture.Client.LookupAsync("Second", default)).Status);
        Assert.Equal(2, fixture.Handler.Requests.Count);
    }

    [Fact]
    public async Task Lookup_BurstAndUtcDailyBudget_DoNotSendExcessRequests()
    {
        var time = new TestClock();
        using var fixture = new Fixture(_ => Json(Valid), clock: time, dailyLimit: 1);
        await fixture.Client.LookupAsync("First", default);
        Assert.Equal(GeocodingStatus.RateLimited, (await fixture.Client.LookupAsync("Second", default)).Status);
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(GeocodingStatus.RateLimited, (await fixture.Client.LookupAsync("Second", default)).Status);
        Assert.Single(fixture.Handler.Requests);
        time.Advance(TimeSpan.FromDays(1));
        Assert.Equal(GeocodingStatus.Found, (await fixture.Client.LookupAsync("Second", default)).Status);
        Assert.Equal(2, fixture.Handler.Requests.Count);
    }

    public static IEnumerable<object[]> InvalidResponses()
    {
        foreach (var value in new[] { "not JSON", "[]", "{}", "{\"results\":null}", "{\"results\":[null]}",
            Valid.Replace("53.349805", "91"), Valid.Replace("-6.26031", "-181"), Valid.Replace("53.349805", "1e999"),
            Valid.Replace("53.349805", "\"53\""), Valid.Replace("synthetic-place", new string('x', 513)) })
            yield return [value, GeocodingStatus.InvalidResponse];
        foreach (var value in new[] { Valid.Replace("building", "country"), Valid.Replace("0.95", "0.2"),
            Valid.Replace("openstreetmap", "unsupported-source"), Valid.Replace("0.95", "null") })
            yield return [value, GeocodingStatus.NoResult];
    }

    [Theory, MemberData(nameof(InvalidResponses))]
    public async Task Lookup_MalformedOutOfRangeOrLowQualityData_DoesNotInventLocation(string json, GeocodingStatus expected)
    {
        using var fixture = new Fixture(_ => Json(json));
        var result = await fixture.Client.LookupAsync("Synthetic", default);
        Assert.Equal(expected, result.Status);
        Assert.Null(result.Location);
        Assert.Single(fixture.Handler.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lookup_OversizedKnownOrUnknownLengthResponse_IsRejected(bool chunked)
    {
        using var fixture = new Fixture(_ => new(HttpStatusCode.OK)
        {
            Content = chunked ? new UnbufferedContent(new string('x', 70000)) : new StringContent(new string('x', 70000))
        });
        Assert.Equal(GeocodingStatus.InvalidResponse, (await fixture.Client.LookupAsync("Synthetic", default)).Status);
    }

    [Fact]
    public async Task Lookup_TimeoutAndCancellation_ReleaseGateAndDoNotLeakException()
    {
        using var state = new GeocodingState();
        using var http = new HttpClient(new Handler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Json(Valid);
        }));
        var client = new GeoapifyGeocodingClient(http, Options.Create(new GeocodingOptions
        { Enabled = true, ApiKey = "test-only-key", TimeoutSeconds = 1 }), state, TimeProvider.System);
        Assert.Equal(GeocodingStatus.Unavailable, (await client.LookupAsync("Synthetic", default)).Status);
        Assert.Equal(1, state.Gate.CurrentCount);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.LookupAsync("Synthetic", canceled.Token));
        Assert.Equal(1, state.Gate.CurrentCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Configuration_KeyRequiredOnlyWhenEnabled(bool enabled)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGeocoding(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Geocoding:Enabled"] = enabled.ToString(), ["Geocoding:TimeoutSeconds"] = "0" }).Build());
        using var provider = services.BuildServiceProvider();
        if (enabled) Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<GeocodingOptions>>().Value);
        else Assert.False(provider.GetRequiredService<IOptions<GeocodingOptions>>().Value.Enabled);
    }

    [Fact]
    public async Task Lookup_InFlightCancellationAndConcurrentCall_AreBoundedAndReleaseGate()
    {
        using var state = new GeocodingState();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async (_, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return Json(Valid);
        }));
        var client = new GeoapifyGeocodingClient(http, Options.Create(new GeocodingOptions
        { Enabled = true, ApiKey = "test-only-key" }), state, TimeProvider.System);
        using var canceled = new CancellationTokenSource();
        var pending = client.LookupAsync("Synthetic", canceled.Token);
        await started.Task;
        Assert.Equal(GeocodingStatus.RateLimited, (await client.LookupAsync("Other", default)).Status);
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, state.Gate.CurrentCount);
    }

    [Fact]
    public async Task Lookup_DailyBudgetCountsRetries_StopsBeforeSecondHttpAttempt()
    {
        using var fixture = new Fixture(_ => new(HttpStatusCode.BadGateway), dailyLimit: 1);
        Assert.Equal(GeocodingStatus.RateLimited, (await fixture.Client.LookupAsync("Synthetic", default)).Status);
        Assert.Single(fixture.Handler.Requests);
    }

    [Fact]
    public void Transport_RegisteredClient_DisablesRedirectsCookiesAndFactoryQueryLogging()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGeocoding(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(nameof(IGeocodingClient));
        while (handler is DelegatingHandler delegating)
        {
            Assert.DoesNotContain("Logging", handler.GetType().Name);
            handler = delegating.InnerHandler!;
        }
        var primary = Assert.IsType<HttpClientHandler>(handler);
        Assert.False(primary.AllowAutoRedirect);
        Assert.False(primary.UseCookies);
    }

    [Fact]
    public void Cache_MoreThanCapacity_EvictsOldestEntry()
    {
        using var state = new GeocodingState();
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < 257; index++)
            state.Put(index.ToString(), new(GeocodingStatus.NoResult), now.AddSeconds(index));
        Assert.Null(state.Get("0", now));
        Assert.NotNull(state.Get("256", now));
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _http;
        private readonly GeocodingState _state = new();
        public Handler Handler { get; }
        public GeoapifyGeocodingClient Client { get; }
        public Fixture(Func<HttpRequestMessage, HttpResponseMessage> response, bool enabled = true, TimeProvider? clock = null, int dailyLimit = 2500)
        {
            Handler = new Handler((request, _) => Task.FromResult(response(request)));
            _http = new HttpClient(Handler);
            Client = new(_http, Options.Create(new GeocodingOptions
            { Enabled = enabled, ApiKey = enabled ? "test-only-key" : null, DailyRequestLimit = dailyLimit }), _state, clock ?? TimeProvider.System);
        }
        public void Dispose() { _http.Dispose(); _state.Dispose(); }
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return response(request, cancellationToken);
        }
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class UnbufferedContent(string text) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(text)).AsTask();
        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)));
    }
}
