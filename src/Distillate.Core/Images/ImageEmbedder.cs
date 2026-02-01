using System.Buffers;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;

namespace Distillate.Core.Images;

public partial class ImageEmbedder
{
    private readonly ILogger<ImageEmbedder> _logger;
    private readonly HttpClient _httpClient;
    private readonly long _maxTotalBytes;

    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".jpg", "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".png", "image/png" },
        { ".gif", "image/gif" },
        { ".webp", "image/webp" },
        { ".svg", "image/svg+xml" },
        { ".ico", "image/x-icon" },
        { ".bmp", "image/bmp" }
    };

    public ImageEmbedder(ILogger<ImageEmbedder> logger, HttpClient httpClient, long maxTotalBytes)
    {
        _logger = logger;
        _httpClient = httpClient;
        _maxTotalBytes = maxTotalBytes;
    }

    public async Task<ImageEmbedResult> EmbedImagesAsync(string html, Uri baseUri, CancellationToken cancellationToken)
    {
        var config = AngleSharp.Configuration.Default;
        var context = BrowsingContext.New(config);
        var parser = context.GetService<IHtmlParser>()!;
        var document = parser.ParseDocument(html);

        var images = document.QuerySelectorAll("img[src]").ToList();
        long totalBytesUsed = 0;
        bool truncated = false;
        int processedCount = 0;

        foreach (var img in images)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var src = img.GetAttribute("src");
            if (string.IsNullOrEmpty(src))
                continue;

            if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                processedCount++;
                continue;
            }

            if (!Uri.TryCreate(baseUri, src, out var imageUri))
                continue;

            if (imageUri.Scheme != "http" && imageUri.Scheme != "https")
                continue;

            try
            {
                var (dataUri, byteCount) = await DownloadAndConvertToDataUriAsync(imageUri, cancellationToken);
                
                if (dataUri == null)
                    continue;

                if (totalBytesUsed + byteCount > _maxTotalBytes)
                {
                    _logger.LogWarning("Image budget exceeded at {BytesUsed} bytes, stopping embedding", totalBytesUsed);
                    truncated = true;
                    break;
                }

                img.SetAttribute("src", dataUri);
                totalBytesUsed += byteCount;
                processedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to embed image: {Url}", imageUri);
            }
        }

        return new ImageEmbedResult
        {
            Html = document.DocumentElement.OuterHtml,
            TotalBytesUsed = totalBytesUsed,
            ImagesProcessed = processedCount,
            Truncated = truncated
        };
    }

    private async Task<(string? DataUri, long ByteCount)> DownloadAndConvertToDataUriAsync(Uri imageUri, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(imageUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
            return (null, 0);

        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (string.IsNullOrEmpty(contentType))
        {
            var extension = Path.GetExtension(imageUri.AbsolutePath);
            contentType = MimeTypes.GetValueOrDefault(extension, "application/octet-stream");
        }

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength > _maxTotalBytes)
        {
            _logger.LogDebug("Image too large: {Bytes} bytes", contentLength);
            return (null, 0);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        
        var bufferSize = contentLength.HasValue ? (int)Math.Min(contentLength.Value, 81920) : 81920;
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        
        try
        {
            using var memoryStream = new MemoryStream();
            int bytesRead;
            
            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, bufferSize), cancellationToken)) > 0)
            {
                await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                
                if (memoryStream.Length > _maxTotalBytes)
                {
                    return (null, 0);
                }
            }

            var bytes = memoryStream.ToArray();
            var base64 = Convert.ToBase64String(bytes);
            var dataUri = $"data:{contentType};base64,{base64}";
            
            return (dataUri, bytes.Length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

public record ImageEmbedResult
{
    public required string Html { get; init; }
    public long TotalBytesUsed { get; init; }
    public int ImagesProcessed { get; init; }
    public bool Truncated { get; init; }
}
