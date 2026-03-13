namespace Distillate.Core.Configuration;

public class DistillateOptions
{
    public const string SectionName = "Distillate";

    public int Concurrency { get; set; } = 1;

    public int MaxTimeMs { get; set; } = 180_000; // 3 minutes

    public long MaxImageBytes { get; set; } = 200 * 1024 * 1024; // 200MB

    public string GcMode { get; set; } = "normal";

    public int StabilizationMs { get; set; } = 10_000; // 10 seconds

    public int MaxUrlLength { get; set; } = 2048;

    public string UserAgent { get; set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    public string ChromiumPath { get; set; } = "/usr/bin/chromium";

    public string? ChromiumArgs { get; set; }

    public int Port { get; set; } = 8080;

    public string LogLevel { get; set; } = "Information";

    public int ScrollDelayMs { get; set; } = 100;

    public int MaxV8HeapSizeMb { get; set; } = 512;

    public bool IsAggressiveGc => string.Equals(GcMode, "aggressive", StringComparison.OrdinalIgnoreCase);
}
