namespace Past.Services;

/// <summary>
/// P0 uses hardcoded sane defaults (no settings UI yet). A count cap stands in
/// for the timed retention that arrives in P1.
/// </summary>
public sealed class HistoryOptions
{
    /// <summary>Maximum clips kept in Recent; oldest are trimmed past this.</summary>
    public int MaxItems { get; set; } = 500;

    /// <summary>Clips larger than this (UTF-16 chars) are ignored, not stored.</summary>
    public int MaxItemChars { get; set; } = 100_000;

    /// <summary>Salt mixed into the dedupe hash. Generated per-install.</summary>
    public string HashSalt { get; set; } = "past";
}
