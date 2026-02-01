using System.Text.Json.Serialization;

namespace Distillate.Core.Models;

/// <summary>
/// Controls how Unicode typographic characters (smart quotes, en/em dashes,
/// ellipsis, etc.) are handled in text outputs.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UnicodeNormalizationMode
{
    /// <summary>
    /// Keep all Unicode characters as-is. Default — preserves typographic quality.
    /// </summary>
    Preserve,

    /// <summary>
    /// Replace smart punctuation with plain ASCII equivalents.
    /// Suitable for legacy pipelines or search/indexing that expect ASCII.
    /// </summary>
    Ascii,

    /// <summary>
    /// Replace smart punctuation with LaTeX-idiomatic sequences
    /// (e.g. en-dash → --, em-dash → ---).
    /// </summary>
    Latex
}
