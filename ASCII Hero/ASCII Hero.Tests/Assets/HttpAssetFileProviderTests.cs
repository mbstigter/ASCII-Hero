using System.Net;
using ASCII_Hero.Client.Game.Assets;

namespace ASCII_Hero.Tests.Assets;

[TestClass]
public sealed class HttpAssetFileProviderTests
{
    /// <summary>Answers every request with a fixed status code and body, without any network.</summary>
    private sealed class FixedResponseHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body) });
    }

    private static HttpAssetFileProvider CreateProvider(HttpStatusCode statusCode, string body = "") =>
        new(new HttpClient(new FixedResponseHandler(statusCode, body)) { BaseAddress = new Uri("http://localhost/") });

    [TestMethod]
    public async Task TryReadTextAsync_Success_ReturnsContent()
    {
        var provider = CreateProvider(HttpStatusCode.OK, "hello");

        Assert.AreEqual("hello", await provider.TryReadTextAsync("Assets/file.ini"));
    }

    [TestMethod]
    public async Task TryReadTextAsync_NotFound_ReturnsNull()
    {
        var provider = CreateProvider(HttpStatusCode.NotFound);

        Assert.IsNull(await provider.TryReadTextAsync("Assets/missing.ini"));
    }

    [TestMethod]
    public async Task TryReadTextAsync_ServerError_Throws()
    {
        var provider = CreateProvider(HttpStatusCode.InternalServerError);

        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => provider.TryReadTextAsync("Assets/file.ini"));
    }
}
