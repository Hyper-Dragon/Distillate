using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Distillate.Tests.Integration;

public class RestModeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public RestModeTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Root_InRestMode_ReturnsJsonServiceInfo()
    {
        var response = await _client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        Assert.True(json.RootElement.TryGetProperty("service", out var service));
        Assert.Equal("Distillate", service.GetString());

        Assert.True(json.RootElement.TryGetProperty("mode", out var mode));
        Assert.Equal("rest", mode.GetString());
    }

    [Fact]
    public async Task Health_InRestMode_ShowsMode()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        Assert.True(json.RootElement.TryGetProperty("mode", out var mode));
        Assert.Equal("rest", mode.GetString());
    }

    [Fact]
    public async Task Live_InRestMode_ReturnsNotFound()
    {
        var formContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("url", "https://example.com")
        });

        var response = await _client.PostAsync("/live", formContent);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
