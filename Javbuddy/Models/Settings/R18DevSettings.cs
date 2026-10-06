using System.ComponentModel.DataAnnotations;

namespace Javbuddy.Models;

/// <summary>Single-row settings for the r18.dev metadata source — a local SQLite import of
/// r18.dev's public database dump, used to look up an actor's full filmography for the
/// "missing movies per actor" feature. Disabled by default: the feature is entirely optional
/// and does nothing (no download, no import, no UI) until enabled here.</summary>
public class R18DevSettings
{
    public int Id { get; set; }

    public bool Enabled { get; set; }

    /// <summary>Overrides the default dump source (https://r18.dev/dumps/latest). Accepts an
    /// http(s) URL or a local file path (plain path or file:// URI) — the latter is how
    /// development/testing points at an already-downloaded dump without hitting the network.</summary>
    [StringLength(500)]
    public string? DumpSourceOverride { get; set; }

    public DateTime? LastImportedAt { get; set; }

    [StringLength(100)]
    public string? LastImportSourceDate { get; set; }

    [StringLength(300)]
    public string? LastImportSummary { get; set; }
}
