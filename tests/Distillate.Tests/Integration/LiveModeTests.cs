using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Distillate.Core.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace Distillate.Tests.Integration;

public class LiveModeTests : IClassFixture<LiveModeTests.LiveModeFactory>
{
    private readonly HttpClient _client;

    public LiveModeTests(LiveModeFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Root_InLiveMode_ReturnsHtmlPage()
    {
        var response = await _client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var contentType = response.Content.Headers.ContentType?.MediaType;
        Assert.Equal("text/html", contentType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Distillate", body);
        Assert.Contains("<form", body);
        Assert.Contains("method=\"get\"", body);
        Assert.Contains("action=\"/live\"", body);
        Assert.Contains("name=\"url\"", body);
        Assert.Contains("name=\"userAgent\"", body);
        Assert.Contains("name=\"referer\"", body);
        Assert.Contains("name=\"cookies\"", body);
        Assert.Contains("name=\"clearCookies\"", body);
        Assert.Contains("name=\"pngConvert\"", body);
        Assert.Contains("id=\"curlOutput\"", body);
        Assert.Contains("readonly", body);
    }

    [Fact]
    public async Task Health_InLiveMode_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        Assert.True(json.RootElement.TryGetProperty("status", out var status));
        Assert.Equal("healthy", status.GetString());

        Assert.True(json.RootElement.TryGetProperty("mode", out var mode));
        Assert.Equal("live", mode.GetString());
    }

    [Fact]
    public async Task Process_InLiveMode_ReturnsNotFound()
    {
        var content = new StringContent("{\"url\":\"https://example.com\"}", System.Text.Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/process", content);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Live_NoUrl_ReturnsFormPage()
    {
        // GET /live without ?url= shows the form (no error message)
        var response = await _client.GetAsync("/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<form", body);
    }

    [Fact]
    public async Task Live_EmptyUrl_ReturnsErrorPage()
    {
        var response = await _client.GetAsync("/live?url=");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Please enter a URL", body);
    }

    [Fact]
    public async Task Live_UrlTooLong_ReturnsErrorPage()
    {
        var longUrl = "https://example.com/" + new string('a', 3000);
        var response = await _client.GetAsync($"/live?url={Uri.EscapeDataString(longUrl)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("exceeds maximum length", body);
    }

    [Fact]
    public async Task Live_InvalidUrl_ReturnsErrorPage()
    {
        var response = await _client.GetAsync("/live?url=not-a-valid-url");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        // Invalid URL should result in an error shown in the HTML page
        Assert.Contains("<html", body);
        Assert.Contains("error", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Live_DirectLink_IncludesUrlInQueryString()
    {
        // Verify that navigating directly to /live?url=... is handled (no server-side 404)
        // The URL itself is invalid so we'll get an error response, but it must be HTML not 404
        var response = await _client.GetAsync("/live?url=https://this-domain-does-not-exist.invalid/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Live_WithUserAgentAndReferer_FormFieldsArePresentOnError()
    {
        // When validation fails the error page should repopulate the advanced fields
        var response = await _client.GetAsync("/live?url=&userAgent=MyAgent%2F1.0&referer=https%3A%2F%2Fexample.com%2F");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("MyAgent/1.0", body);
        Assert.Contains("https://example.com/", body);
    }

    [Fact]
    public async Task Live_EmptyUserAgentAndReferer_FormHasFields()
    {
        // The form must always contain user-agent, referer, cookies, clearCookies, and pngConvert inputs
        var response = await _client.GetAsync("/live");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("name=\"userAgent\"", body);
        Assert.Contains("name=\"referer\"", body);
        Assert.Contains("name=\"cookies\"", body);
        Assert.Contains("name=\"clearCookies\"", body);
        Assert.Contains("name=\"pngConvert\"", body);
    }

    [Fact]
    public async Task Live_ClearCookiesChecked_CheckboxRepopulatedOnError()
    {
        // When validation fails with clearCookies=on the error page must keep the checkbox checked
        var response = await _client.GetAsync("/live?url=&clearCookies=on");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        // The rendered checkbox should carry the "checked" attribute
        Assert.Contains("checked", body);
    }

    [Fact]
    public async Task Live_PngConvertChecked_CheckboxRepopulatedOnError()
    {
        // When validation fails with pngConvert=on the error page must keep the checkbox checked
        var response = await _client.GetAsync("/live?url=&pngConvert=on");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("checked", body);
        Assert.Contains("Convert images to PNG", body);
    }

    [Fact]
    public async Task Live_WithCookies_CookiesRepopulatedOnError()
    {
        // When validation fails the cookies textarea should be repopulated
        var cookies = Uri.EscapeDataString("session=abc123\ntoken=xyz");
        var response = await _client.GetAsync($"/live?url=&cookies={cookies}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("session=abc123", body);
        Assert.Contains("token=xyz", body);
    }

    // --- Console panel tests ---

    [Fact]
    public async Task Root_InLiveMode_ContainsConsoleSection()
    {
        var response = await _client.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("id=\"consoleSection\"", body);
        Assert.Contains("id=\"consoleOutput\"", body);
        Assert.Contains("Console Output", body);
        Assert.Contains("console-output", body);
    }

    // --- Two-column layout tests ---

    [Fact]
    public async Task Root_InLiveMode_HasTwoColumnLayout()
    {
        var response = await _client.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("main-content", body);
        Assert.Contains("left-column", body);
        Assert.Contains("right-column", body);
        Assert.Contains("grid-template-columns", body);
    }

    // --- Extended curl builder tests ---

    [Fact]
    public async Task Root_InLiveMode_CurlBuilderHasAllContentFlags()
    {
        var response = await _client.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("id=\"curl_includeSourceHtml\"", body);
        Assert.Contains("id=\"curl_includeCleanHtmlEmbeddedImages\"", body);
        Assert.Contains("id=\"curl_includeCleanHtmlLinkedImages\"", body);
        Assert.Contains("id=\"curl_includeCleanHtmlNoImages\"", body);
        Assert.Contains("id=\"curl_includeMarkdownLinkedImages\"", body);
        Assert.Contains("id=\"curl_includeMarkdownNoImages\"", body);
        Assert.Contains("id=\"curl_includePlainText\"", body);
    }

    [Fact]
    public async Task Root_InLiveMode_CurlBuilderHasLinkExtractionFlags()
    {
        var response = await _client.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("id=\"curl_includeAllLinksInPage\"", body);
        Assert.Contains("id=\"curl_includeAllLinksMedia\"", body);
        Assert.Contains("id=\"curl_includeAllLinksAssets\"", body);
    }

    [Fact]
    public async Task Root_InLiveMode_CurlBuilderHasImageFlags()
    {
        var response = await _client.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("id=\"curl_includeAllInPageImages\"", body);
        Assert.Contains("id=\"curl_includeAllOutOfPageImages\"", body);
        Assert.Contains("id=\"curl_imageLinkRewrite\"", body);
    }

    [Fact]
    public async Task Root_InLiveMode_CurlBuilderHasProcessingOptions()
    {
        var response = await _client.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("id=\"curl_engine\"", body);
        Assert.Contains("id=\"curl_timeoutMs\"", body);
        Assert.Contains("id=\"curl_stabilizationMs\"", body);
        Assert.Contains("id=\"curl_unicodeNormalization\"", body);
        Assert.Contains("id=\"curl_includeDebugInfo\"", body);
    }

    [Fact]
    public async Task Root_InLiveMode_CurlBuilderHasUnicodeOptions()
    {
        var response = await _client.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("value=\"preserve\"", body);
        Assert.Contains("value=\"ascii\"", body);
        Assert.Contains("value=\"latex\"", body);
    }

    // --- Settings persistence (localStorage) tests ---

    [Fact]
    public async Task Root_InLiveMode_HasLocalStoragePersistenceScript()
    {
        var response = await _client.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("distillate-settings", body);
        Assert.Contains("localStorage", body);
        Assert.Contains("saveSettings", body);
        Assert.Contains("loadSettings", body);
    }

    // --- SSE endpoint tests ---

    [Fact]
    public async Task LiveEvents_NoUrl_ReturnsSseStream()
    {
        var response = await _client.GetAsync("/live/events");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task LiveEvents_NoUrl_ContainsFailEvent()
    {
        var response = await _client.GetAsync("/live/events");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("event: fail", body);
        Assert.Contains("Please enter a URL", body);
    }

    [Fact]
    public async Task LiveEvents_EmptyUrl_ContainsFailEvent()
    {
        var response = await _client.GetAsync("/live/events?url=");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("event: fail", body);
        Assert.Contains("Please enter a URL", body);
    }

    [Fact]
    public async Task LiveEvents_UrlTooLong_ContainsFailEvent()
    {
        var longUrl = "https://example.com/" + new string('a', 3000);
        var response = await _client.GetAsync($"/live/events?url={Uri.EscapeDataString(longUrl)}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("event: fail", body);
        Assert.Contains("exceeds maximum length", body);
    }

    [Fact]
    public async Task LiveEvents_ValidUrl_ContainsLogEvents()
    {
        var response = await _client.GetAsync("/live/events?url=https://this-domain-does-not-exist.invalid/");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("event: log", body);
        Assert.Contains("Validating parameters", body);
    }

    [Fact]
    public async Task LiveEvents_InvalidUrl_StreamsLogThenError()
    {
        var response = await _client.GetAsync("/live/events?url=not-a-valid-url");
        var body = await response.Content.ReadAsStringAsync();

        // Should contain log events followed by a fail event
        Assert.Contains("event: log", body);
        // The URL will fail during processing, producing either a fail or error log
        Assert.True(body.Contains("event: fail") || body.Contains("ERROR"), "Expected a fail event or ERROR log for invalid URL");
    }

    // --- Form submit interception (EventSource) test ---

    [Fact]
    public async Task Root_InLiveMode_HasEventSourceScript()
    {
        var response = await _client.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("EventSource", body);
        Assert.Contains("/live/events", body);
        Assert.Contains("extractBtn", body);
    }

    // --- Curl file save commands test ---

    [Fact]
    public async Task Root_InLiveMode_CurlBuilderHasFileSaveLogic()
    {
        var response = await _client.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        // The JS should contain file output mappings
        Assert.Contains("response.json", body);
        Assert.Contains("article-embedded.html", body);
        Assert.Contains("article.md", body);
        Assert.Contains("article.txt", body);
        Assert.Contains("responseField", body);
    }

    public class LiveModeFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("MODE", "live");
            builder.ConfigureServices(services =>
            {
                services.Configure<DistillateOptions>(options =>
                {
                    options.Mode = "live";
                });
            });
        }
    }
}
