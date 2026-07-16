namespace Past.Core;

/// <summary>
/// Raw capture handed to the history service before dedupe/caps/persistence.
/// Produced by the platform clipboard monitor.
/// </summary>
public sealed record ClipDraft(string Content, string? SourceApp);
