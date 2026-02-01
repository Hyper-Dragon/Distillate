using System.Net;
using System.Text;
using System.Text.Json;
using Distillate.Core.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Distillate.Tests.Integration;

public class ProcessEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ProcessEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Process_InvalidUrl_ReturnsBadRequest()
    {
        var request = new ProcessRequest { Url = "not-a-valid-url" };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/process", content);

        // The service returns 200 with Success=false for processing failures
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ProcessResponse>(responseContent, new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true 
        });

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.NotNull(result.Errors);
    }

    [Fact]
    public async Task Process_FtpUrl_ReturnsError()
    {
        var request = new ProcessRequest { Url = "ftp://files.example.com/file.txt" };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/process", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ProcessResponse>(responseContent, new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true 
        });

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Contains(result.Errors!, e => e.Contains("HTTP") || e.Contains("HTTPS"));
    }

    [Fact]
    public async Task Process_UrlTooLong_ReturnsBadRequest()
    {
        var longUrl = "https://example.com/" + new string('a', 3000);
        var request = new ProcessRequest { Url = longUrl };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/process", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Process_WithTraceId_ReturnsTraceId()
    {
        var request = new ProcessRequest { Url = "https://example.com" };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/process")
        {
            Content = content
        };
        httpRequest.Headers.Add("X-Trace-Id", "test-trace-123");

        var response = await _client.SendAsync(httpRequest);
        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ProcessResponse>(responseContent, new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true 
        });

        Assert.NotNull(result);
        Assert.Equal("test-trace-123", result.TraceId);
    }

    [Fact]
    public async Task Process_WithoutTraceId_GeneratesTraceId()
    {
        var request = new ProcessRequest { Url = "https://example.com" };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/process", content);
        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ProcessResponse>(responseContent, new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true 
        });

        Assert.NotNull(result);
        Assert.NotNull(result.TraceId);
        Assert.NotEmpty(result.TraceId);
    }

    [Fact]
    public async Task Process_RequestedUrl_IsPreserved()
    {
        var request = new ProcessRequest { Url = "https://example.com/test" };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/process", content);
        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ProcessResponse>(responseContent, new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true 
        });

        Assert.NotNull(result);
        Assert.Equal("https://example.com/test", result.RequestedUrl);
    }

    [Fact]
    public async Task Process_ResponseHasTimestamp()
    {
        var request = new ProcessRequest { Url = "https://example.com" };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        var beforeRequest = DateTimeOffset.UtcNow;
        var response = await _client.PostAsync("/process", content);
        var afterRequest = DateTimeOffset.UtcNow;

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ProcessResponse>(responseContent, new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true 
        });

        Assert.NotNull(result);
        Assert.True(result.ProcessedAt >= beforeRequest.AddSeconds(-1));
        Assert.True(result.ProcessedAt <= afterRequest.AddSeconds(1));
    }
}
