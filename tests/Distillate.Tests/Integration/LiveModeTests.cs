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
        Assert.Contains("action=\"/live\"", body);
        Assert.Contains("name=\"url\"", body);
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
    public async Task Live_EmptyUrl_ReturnsErrorPage()
    {
        var formContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("url", "")
        });

        var response = await _client.PostAsync("/live", formContent);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Please enter a URL", body);
    }

    [Fact]
    public async Task Live_UrlTooLong_ReturnsErrorPage()
    {
        var longUrl = "https://example.com/" + new string('a', 3000);
        var formContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("url", longUrl)
        });

        var response = await _client.PostAsync("/live", formContent);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("exceeds maximum length", body);
    }

    [Fact]
    public async Task Live_InvalidUrl_ReturnsErrorPage()
    {
        var formContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("url", "not-a-valid-url")
        });

        var response = await _client.PostAsync("/live", formContent);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("text/html", response.Content.Headers.ContentType?.MediaType);
        // Invalid URL should result in an error shown in the HTML page
        Assert.Contains("<html", body);
        Assert.Contains("error", body, StringComparison.OrdinalIgnoreCase);
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
