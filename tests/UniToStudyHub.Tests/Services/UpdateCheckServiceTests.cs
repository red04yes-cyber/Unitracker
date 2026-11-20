using System.Net;
using System.Text;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class UpdateCheckServiceTests
{
    [Fact]
    public async Task ReturnsNewerStableReleaseFromTrustedGitHubOrigin()
    {
        using var client = CreateClient("""
            {"tag_name":"v0.3.0","draft":false,"prerelease":false,
             "html_url":"https://attacker.invalid/redirect"}
            """);
        var service = new UpdateCheckService(client);

        var update = await service.CheckForUpdateAsync(new Version(0, 2, 0));

        Assert.NotNull(update);
        Assert.Equal(new Version(0, 3, 0), update.Version);
        Assert.Equal("v0.3.0", update.TagName);
        Assert.Equal("github.com", update.ReleaseUri.Host);
        Assert.Equal("https", update.ReleaseUri.Scheme);
        Assert.Equal("/red04yes-cyber/Unitracker/releases/tag/v0.3.0", update.ReleaseUri.AbsolutePath);
    }

    [Theory]
    [InlineData("v0.2.0")]
    [InlineData("v0.1.9")]
    public async Task DoesNotNotifyForSameOrOlderVersion(string tag)
    {
        using var client = CreateClient($$"""{"tag_name":"{{tag}}","draft":false,"prerelease":false}""");

        var update = await new UpdateCheckService(client)
            .CheckForUpdateAsync(new Version(0, 2, 0));

        Assert.Null(update);
    }

    [Theory]
    [InlineData("""{"tag_name":"v0.3.0","draft":true,"prerelease":false}""")]
    [InlineData("""{"tag_name":"v0.3.0","draft":false,"prerelease":true}""")]
    [InlineData("""{"tag_name":"latest","draft":false,"prerelease":false}""")]
    [InlineData("""{"tag_name":"v999999999999999999999.0.0","draft":false,"prerelease":false}""")]
    public async Task IgnoresDraftPrereleaseAndInvalidTags(string json)
    {
        using var client = CreateClient(json);

        var update = await new UpdateCheckService(client)
            .CheckForUpdateAsync(new Version(0, 2, 0));

        Assert.Null(update);
    }

    [Fact]
    public async Task TreatsMissingReleaseAsNoUpdate()
    {
        using var client = new HttpClient(new StubHandler(HttpStatusCode.NotFound, string.Empty));

        var update = await new UpdateCheckService(client)
            .CheckForUpdateAsync(new Version(0, 2, 0));

        Assert.Null(update);
    }

    [Fact]
    public async Task RejectsOversizedReleaseMetadata()
    {
        var content = new ByteArrayContent(new byte[256 * 1024 + 1]);
        using var client = new HttpClient(new StubHandler(HttpStatusCode.OK, content));

        var update = await new UpdateCheckService(client)
            .CheckForUpdateAsync(new Version(0, 2, 0));

        Assert.Null(update);
    }

    [Fact]
    public async Task PropagatesCallerCancellation()
    {
        using var client = new HttpClient(new StubHandler(async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new UpdateCheckService(client).CheckForUpdateAsync(new Version(0, 2, 0), cancellation.Token));
    }

    private static HttpClient CreateClient(string json) =>
        new(new StubHandler(HttpStatusCode.OK, json));

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<CancellationToken, Task<HttpResponseMessage>> _response;

        public StubHandler(HttpStatusCode statusCode, string body)
            : this(_ => Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            }))
        {
        }

        public StubHandler(HttpStatusCode statusCode, HttpContent content)
            : this(_ => Task.FromResult(new HttpResponseMessage(statusCode) { Content = content }))
        {
        }

        public StubHandler(Func<CancellationToken, Task<HttpResponseMessage>> response) =>
            _response = response;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("api.github.com", request.RequestUri?.Host);
            Assert.NotEmpty(request.Headers.UserAgent);
            return _response(cancellationToken);
        }
    }
}
