using System.Net;
using System.Text.Json;
using Distillate.Core.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Distillate.Tests.Integration;

public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_ReturnsCorrectShape()
    {
        var response = await _client.GetAsync("/health");
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        Assert.True(json.RootElement.TryGetProperty("status", out var status));
        Assert.Equal("healthy", status.GetString());

        Assert.True(json.RootElement.TryGetProperty("timestamp", out _));

        Assert.True(json.RootElement.TryGetProperty("concurrency", out var concurrency));
        Assert.True(concurrency.TryGetProperty("max", out _));
        Assert.True(concurrency.TryGetProperty("available", out _));
    }
}
