using System.Net;
using System.Text;
using Snaploom.App;

namespace Snaploom.IntegrationTests;

public sealed class UpdateCheckServiceTests
{
    [Fact]
    public void ConstructingTheServiceDoesNotAccessTheNetwork()
    {
        var handler = new RecordingHandler(_ => CreateReleaseResponse("v1.2.0"));

        _ = CreateService(handler, new Version(1, 0, 0));

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task NewerReleaseReturnsVersionDateNotesAndDownloadPage()
    {
        var handler = new RecordingHandler(
            _ => CreateReleaseResponse(
                "snaploom-v1.4.2",
                body: "## Improvements\n- Faster capture",
                publishedAt: "2026-07-10T08:30:00Z"));
        var service = CreateService(handler, new Version(1, 3, 0));

        var result = await service.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal(new Version(1, 4, 2, 0), result.LatestVersion);
        Assert.Equal(new DateTimeOffset(2026, 7, 10, 8, 30, 0, TimeSpan.Zero), result.PublishedAt);
        Assert.Equal("## Improvements\n- Faster capture", result.ReleaseNotes);
        Assert.Equal(
            new Uri("https://github.com/liuchuana/Snaploom/releases/tag/v1.4.2"),
            result.ReleasePage);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(
            "https://api.github.com/repos/liuchuana/Snaploom/releases/latest",
            handler.LastRequestUri?.AbsoluteUri);
    }

    [Theory]
    [InlineData("v1.4.2", 1, 4, 2, 0)]
    [InlineData("v1.4.1", 1, 4, 2, 0)]
    public async Task EqualOrOlderReleaseReportsUpToDate(
        string tag,
        int major,
        int minor,
        int build,
        int revision)
    {
        var service = CreateService(
            new RecordingHandler(_ => CreateReleaseResponse(tag)),
            new Version(major, minor, build, revision));

        var result = await service.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task GitHubRateLimitHasADistinctResult(HttpStatusCode statusCode)
    {
        var handler = new RecordingHandler(
            _ =>
            {
                var response = new HttpResponseMessage(statusCode);
                response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
                return response;
            });

        var result = await CreateService(handler, new Version(1, 0, 0))
            .CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.RateLimited, result.Status);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"tag_name\":\"not-a-version\"}")]
    [InlineData("{\"tag_name\":\"v2.0.0\",\"published_at\":null,\"html_url\":\"javascript:bad\"}")]
    public async Task InvalidApiResponseHasADistinctResult(string json)
    {
        var handler = new RecordingHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });

        var result = await CreateService(handler, new Version(1, 0, 0))
            .CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.InvalidResponse, result.Status);
    }

    [Fact]
    public async Task NetworkFailureHasADistinctResult()
    {
        var handler = new RecordingHandler(
            _ => throw new HttpRequestException("offline"));

        var result = await CreateService(handler, new Version(1, 0, 0))
            .CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.NetworkFailure, result.Status);
    }

    private static UpdateCheckService CreateService(
        HttpMessageHandler handler,
        Version currentVersion) => new(new HttpClient(handler), currentVersion);

    private static HttpResponseMessage CreateReleaseResponse(
        string tag,
        string body = "Release notes",
        string publishedAt = "2026-07-10T08:30:00Z") => new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""
                {
                  "tag_name": "{{tag}}",
                  "published_at": "{{publishedAt}}",
                  "body": {{System.Text.Json.JsonSerializer.Serialize(body)}},
                  "html_url": "https://github.com/liuchuana/Snaploom/releases/tag/v1.4.2"
                }
                """,
                Encoding.UTF8,
                "application/json"),
        };

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestUri = request.RequestUri;
            return Task.FromResult(responseFactory(request));
        }
    }
}
