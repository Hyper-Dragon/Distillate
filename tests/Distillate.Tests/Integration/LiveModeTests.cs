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
        Assert.Contains("name=\"clearCookies\"", body);
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
        // The form must always contain user-agent, referer, and clearCookies inputs
        var response = await _client.GetAsync("/live");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("name=\"userAgent\"", body);
        Assert.Contains("name=\"referer\"", body);
        Assert.Contains("name=\"clearCookies\"", body);
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
