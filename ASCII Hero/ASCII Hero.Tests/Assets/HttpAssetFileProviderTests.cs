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

    /// <summary>Serves a fixed set of paths (404 for anything else) and records every request.</summary>
    private sealed class PathHandler(Dictionary<string, string> files) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            Requests.Add(path);
            return Task.FromResult(files.TryGetValue(path, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static HttpAssetFileProvider CreateProvider(PathHandler handler, string? manifestPath) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }, manifestPath);

    [TestMethod]
    public async Task TryReadTextAsync_WithManifest_UnlistedFileIsAbsentWithoutRequest()
    {
        var handler = new PathHandler(new()
        {
            ["Assets/manifest.txt"] = "Assets/a.ini\r\nAssets/b.ini\r\n",
            ["Assets/a.ini"] = "A",
        });
        var provider = CreateProvider(handler, "Assets/manifest.txt");

        Assert.AreEqual("A", await provider.TryReadTextAsync("Assets/a.ini"));
        Assert.IsNull(await provider.TryReadTextAsync("Assets/other.ini"));
        CollectionAssert.AreEqual(new[] { "Assets/manifest.txt", "Assets/a.ini" }, handler.Requests);
    }

    [TestMethod]
    public async Task TryReadTextAsync_WithManifest_ListedButMissingFileIsStillAbsent()
    {
        var handler = new PathHandler(new() { ["Assets/manifest.txt"] = "Assets/b.ini\n" });
        var provider = CreateProvider(handler, "Assets/manifest.txt");

        Assert.IsNull(await provider.TryReadTextAsync("Assets/b.ini"));
    }

    [TestMethod]
    public async Task TryReadTextAsync_ManifestMissing_RequestsEveryFile()
    {
        var handler = new PathHandler(new() { ["Assets/a.ini"] = "A" });
        var provider = CreateProvider(handler, "Assets/manifest.txt");

        Assert.AreEqual("A", await provider.TryReadTextAsync("Assets/a.ini"));
        Assert.IsNull(await provider.TryReadTextAsync("Assets/other.ini"));
        CollectionAssert.AreEqual(new[] { "Assets/manifest.txt", "Assets/a.ini", "Assets/other.ini" }, handler.Requests);
    }

    [TestMethod]
    public async Task TryReadTextAsync_ServerError_Throws()
    {
        var provider = CreateProvider(HttpStatusCode.InternalServerError);

        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => provider.TryReadTextAsync("Assets/file.ini"));
    }
}
