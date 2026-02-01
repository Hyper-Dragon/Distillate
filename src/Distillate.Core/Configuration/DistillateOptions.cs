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

    /// <summary>
    /// Retry-After, in seconds, sent with a capacity refusal. Callers that treat "busy" and "unreachable"
    /// the same way cannot tell a queue that needs to drain from a dependency that has gone, so the
    /// refusal says how long to wait rather than leaving it to be guessed.
    /// </summary>
    public int BusyRetryAfterSeconds { get; set; } = 15;

    public string UserAgent { get; set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    public string ChromiumPath { get; set; } = "/usr/bin/chromium";

    public string? ChromiumArgs { get; set; }

    public int Port { get; set; } = 8080;

    public int ScrollDelayMs { get; set; } = 100;

    public int MaxV8HeapSizeMb { get; set; } = 512;

    /// <summary>
    /// Service mode: "rest" (default) for JSON API, or "live" for interactive web UI.
    /// The two modes are mutually exclusive — the service runs in one mode only.
    /// </summary>
    public string Mode { get; set; } = "rest";

    public bool IsAggressiveGc => string.Equals(GcMode, "aggressive", StringComparison.OrdinalIgnoreCase);

    public bool IsLiveMode => string.Equals(Mode, "live", StringComparison.OrdinalIgnoreCase);
}
