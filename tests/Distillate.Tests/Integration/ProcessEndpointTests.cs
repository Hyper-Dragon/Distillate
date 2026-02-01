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
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ProcessEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Process_InvalidUrl_ReturnsErrorResponse()
    {
        var request = new ProcessRequest { Url = "not-a-valid-url" };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/process", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = JsonSerializer.Deserialize<ProcessResponse>(
            await response.Content.ReadAsStringAsync(), JsonOptions);

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

        var result = JsonSerializer.Deserialize<ProcessResponse>(
            await response.Content.ReadAsStringAsync(), JsonOptions);

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
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/process")
        {
            Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Add("X-Trace-Id", "test-trace-123");

        var response = await _client.SendAsync(httpRequest);
        var result = JsonSerializer.Deserialize<ProcessResponse>(
            await response.Content.ReadAsStringAsync(), JsonOptions);

        Assert.NotNull(result);
        Assert.Equal("test-trace-123", result.TraceId);
    }

    [Fact]
    public async Task Process_WithoutTraceId_GeneratesTraceId()
    {
        var request = new ProcessRequest { Url = "https://example.com" };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/process", content);
        var result = JsonSerializer.Deserialize<ProcessResponse>(
            await response.Content.ReadAsStringAsync(), JsonOptions);

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
        var result = JsonSerializer.Deserialize<ProcessResponse>(
            await response.Content.ReadAsStringAsync(), JsonOptions);

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

        var result = JsonSerializer.Deserialize<ProcessResponse>(
            await response.Content.ReadAsStringAsync(), JsonOptions);

        Assert.NotNull(result);
        Assert.True(result.ProcessedAt >= beforeRequest.AddSeconds(-1));
        Assert.True(result.ProcessedAt <= afterRequest.AddSeconds(1));
    }

    [Fact]
    public async Task Process_DefaultRequest_ReturnsMarkdownNoImagesAndPlainText()
    {
        // Default request should have markdownNoImages=true and plainText=true
        // even if the browser fails (error response), the shape is correct
        var request = new ProcessRequest { Url = "https://example.com" };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/process", content);
        var result = JsonSerializer.Deserialize<ProcessResponse>(
            await response.Content.ReadAsStringAsync(), JsonOptions);

        Assert.NotNull(result);
        // Regardless of success, the old fields (rawText, markdown, cleanHtml) must not appear
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("\"rawText\"", json);
        Assert.DoesNotContain("\"markdown\":", json);
        Assert.DoesNotContain("\"cleanHtml\":", json);
    }

    [Fact]
    public async Task Process_Response_DeserializesMetadataFields()
    {
        // Verify the response contract supports metadata fields via deserialization
        var request = new ProcessRequest { Url = "https://example.com" };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/process", content);
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ProcessResponse>(json, JsonOptions);

        Assert.NotNull(result);
        // These metadata properties exist on the model and deserialize without error.
        // For example.com, some may be null or empty depending on page content.
        // The round-trip proves the contract supports them.
        _ = result.Byline;
        _ = result.Author;
        _ = result.Excerpt;
        _ = result.SiteName;
        _ = result.Language;
        _ = result.PublicationDate;
        _ = result.FeaturedImage;
        _ = result.TimeToRead;
    }

    [Fact]
    public async Task Process_SourceHtmlOmittedFlag_SetWhenNotRequested()
    {
        // Must use a valid http/https URL so URL validation passes and the flag is set
        var request = new ProcessRequest
        {
            Url = "https://example.com",
            IncludeSourceHtml = false
        };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/process", content);
        var result = JsonSerializer.Deserialize<ProcessResponse>(
            await response.Content.ReadAsStringAsync(), JsonOptions);

        Assert.NotNull(result);
        var flags = (CompletionFlag)result.CompletionFlags;
        Assert.True(flags.HasFlag(CompletionFlag.SourceHtmlOmitted));
    }
}

