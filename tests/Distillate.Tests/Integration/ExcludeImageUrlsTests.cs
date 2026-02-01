using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Distillate.Core.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Distillate.Tests.Integration;

/// <summary>
/// End-to-end coverage for excludeImageUrls and the ImageExclusionApplied (128) completion flag.
/// The pages are served from a loopback TCP listener, so the tests are deterministic and do not
/// depend on any third-party site: an excluded image must vanish from every output, consume no
/// image-NNNN slot, never be downloaded, and set flag 128.
/// </summary>
public class ExcludeImageUrlsTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // 1x1 PNG.
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private readonly HttpClient _client;
    private readonly StaticSiteServer _site;

    public ExcludeImageUrlsTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
        _site = new StaticSiteServer();

        _site.Add("/article.html", "text/html", Encoding.UTF8.GetBytes(
            string.Format(ArticleWithImages, _site.BaseUrl)));
        _site.Add("/noimg.html", "text/html", Encoding.UTF8.GetBytes(
            string.Format(ArticleWithoutImages, _site.BaseUrl)));

        foreach (var path in new[] { "/img/hero.png", "/img/donate.png", "/img/logo.png" })
            _site.Add(path, "image/png", PngBytes);
    }

    public void Dispose() => _site.Dispose();

    // --- Featured-image fallback (the shape that under-reported the flag) ---

    [Fact]
    public async Task NoExclusion_FeaturedFallbackStillInjectsImage()
    {
        var result = await PostAsync(new ProcessRequest
        {
            Url = _site.Url("/noimg.html"),
            IncludeMarkdownLinkedImages = true,
            IncludeMarkdownNoImages = false,
            IncludePlainText = false,
            StabilizationMs = 0
        });

        Assert.True(result.Success);
        var image = Assert.Single(result.AllInPageImages!);
        Assert.Equal(_site.Url("/img/logo.png"), image.OriginalUrl);
        Assert.Equal("image-0000.png", image.LocalName);
        Assert.Contains("![", result.MarkdownLinkedImages);
        Assert.False(HasFlag(result, CompletionFlag.ImageExclusionApplied));
    }

    [Fact]
    public async Task ExcludedFeaturedImage_SuppressesFallbackAndSetsFlag()
    {
        var result = await PostAsync(new ProcessRequest
        {
            Url = _site.Url("/noimg.html"),
            IncludeMarkdownLinkedImages = true,
            IncludeMarkdownNoImages = false,
            IncludePlainText = false,
            StabilizationMs = 0,
            ExcludeImageUrls = ["/logo"]
        });

        Assert.True(result.Success);
        Assert.True(result.AllInPageImages is null || result.AllInPageImages.Length == 0);
        Assert.DoesNotContain("![", result.MarkdownLinkedImages);
        Assert.True(HasFlag(result, CompletionFlag.ImageExclusionApplied));
    }

    // --- In-page document removal ---

    [Fact]
    public async Task ExcludedInPageImage_RemovedFromOutputsAndSetsFlag()
    {
        var result = await PostAsync(new ProcessRequest
        {
            Url = _site.Url("/article.html"),
            IncludeCleanHtmlLinkedImages = true,
            IncludeMarkdownLinkedImages = true,
            IncludeAllInPageImages = true,
            ImageLinkRewrite = true,
            IncludeMarkdownNoImages = false,
            IncludePlainText = false,
            StabilizationMs = 0,
            ExcludeImageUrls = ["/donate"]
        });

        Assert.True(result.Success);

        // The surviving image keeps a contiguous slot: the excluded one consumed none.
        var image = Assert.Single(result.AllInPageImages!);
        Assert.Equal(_site.Url("/img/hero.png"), image.OriginalUrl);
        Assert.Equal("image-0000.png", image.LocalName);

        Assert.NotNull(result.CleanHtmlLinkedImages);
        Assert.DoesNotContain("donate", result.CleanHtmlLinkedImages, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("donate", result.MarkdownLinkedImages, StringComparison.OrdinalIgnoreCase);
        Assert.True(HasFlag(result, CompletionFlag.ImageExclusionApplied));
    }

    [Fact]
    public async Task NonMatchingExclusion_LeavesOutputsAndFlagUntouched()
    {
        var result = await PostAsync(new ProcessRequest
        {
            Url = _site.Url("/article.html"),
            IncludeAllInPageImages = true,
            IncludeMarkdownNoImages = false,
            IncludePlainText = false,
            StabilizationMs = 0,
            ExcludeImageUrls = ["/nothing-matches-this-xyzzy"]
        });

        Assert.True(result.Success);
        Assert.Equal(2, result.AllInPageImages!.Length);
        Assert.False(HasFlag(result, CompletionFlag.ImageExclusionApplied));
    }

    // --- Helpers ---

    private static bool HasFlag(ProcessResponse response, CompletionFlag flag) =>
        ((CompletionFlag)response.CompletionFlags).HasFlag(flag);

    private async Task<ProcessResponse> PostAsync(ProcessRequest request)
    {
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/process", content);
        response.EnsureSuccessStatusCode();

        var result = JsonSerializer.Deserialize<ProcessResponse>(
            await response.Content.ReadAsStringAsync(), JsonOptions);

        Assert.NotNull(result);
        return result!;
    }

    private const string ArticleWithImages = """
        <!DOCTYPE html>
        <html lang="en">
        <head><meta charset="utf-8" /><title>Exclusion Test Article</title></head>
        <body>
          <main>
            <article>
              <h1>Exclusion Test Article</h1>
              <p>First paragraph of the test article body, long enough for the reader extractor to accept it as content. It continues with more words so the paragraph is substantial and not pruned.</p>
              <p><img src="{0}/img/hero.png" alt="Hero image" /></p>
              <p>Second paragraph goes here with plenty of ordinary prose to keep the extraction happy and to make sure the article element is chosen as the main content container by the reader algorithm.</p>
              <p><img src="{0}/img/donate.png" alt="Donate today" /></p>
              <p>Third paragraph wraps up the article body with a closing thought and a few more words for good measure, ensuring the extractor retains everything above.</p>
            </article>
          </main>
        </body>
        </html>
        """;

    private const string ArticleWithoutImages = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <title>No Image Article</title>
          <meta property="og:image" content="{0}/img/logo.png" />
          <link rel="image_src" href="{0}/img/logo.png" />
        </head>
        <body>
          <main>
            <article>
              <h1>No Image Article</h1>
              <p>First paragraph of the test article body, long enough for the reader extractor to accept it as content. It continues with more words so the paragraph is substantial and not pruned.</p>
              <p>Second paragraph goes here with plenty of ordinary prose to keep the extraction happy and to make sure the article element is chosen as the main content container by the reader algorithm.</p>
              <p>Third paragraph wraps up the article body with a closing thought and a few more words for good measure, ensuring the extractor retains everything above.</p>
            </article>
          </main>
        </body>
        </html>
        """;

    /// <summary>
    /// Minimal loopback HTTP/1.1 server for fixtures. Chromium fetches these pages, so the routes
    /// are served over a real socket rather than through the test host.
    /// </summary>
    private sealed class StaticSiteServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly ConcurrentDictionary<string, (string ContentType, byte[] Body)> _routes = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _acceptLoop;

        public StaticSiteServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
        }

        public int Port { get; }

        public string BaseUrl => $"http://127.0.0.1:{Port}";

        public string Url(string path) => BaseUrl + path;

        public void Add(string path, string contentType, byte[] body) => _routes[path] = (contentType, body);

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                _ = Task.Run(() => HandleAsync(client, cancellationToken), CancellationToken.None);
            }
        }

        private async Task HandleAsync(TcpClient client, CancellationToken cancellationToken)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);

                    var requestLine = await reader.ReadLineAsync(cancellationToken);
                    if (string.IsNullOrEmpty(requestLine))
                        return;

                    // Drain headers.
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellationToken)))
                    {
                    }

                    var parts = requestLine.Split(' ');
                    var path = parts.Length > 1 ? parts[1].Split('?')[0] : "/";

                    if (!_routes.TryGetValue(path, out var route))
                    {
                        await WriteAsync(stream, "404 Not Found", "text/plain", "not found"u8.ToArray(), cancellationToken);
                        return;
                    }

                    await WriteAsync(stream, "200 OK", route.ContentType, route.Body, cancellationToken);
                }
                catch
                {
                    // Client aborted (Chromium opens speculative connections) — nothing to do.
                }
            }
        }

        private static async Task WriteAsync(
            NetworkStream stream, string status, string contentType, byte[] body, CancellationToken cancellationToken)
        {
            var header = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header, cancellationToken);
            await stream.WriteAsync(body, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            try
            {
                _acceptLoop.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // Ignore shutdown races.
            }
            _cts.Dispose();
        }
    }
}
