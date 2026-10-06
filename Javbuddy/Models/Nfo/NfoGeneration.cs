namespace Javbuddy.Models;

/// <summary>One earlier version of a movie's .nfo: the file's content right before a
/// Javbuddy write replaced it, kept in the database instead of a ".nfo.bak" next to the file.
/// Only the newest NfoHistoryService.GenerationsKept per movie are kept.</summary>
public class NfoGeneration
{
    public int Id { get; set; }

    public int MovieId { get; set; }
    public Movie Movie { get; set; } = null!;

    /// <summary>When this version was replaced on disk.</summary>
    public DateTime ReplacedAtUtc { get; set; }

    /// <summary>The write that replaced this version.</summary>
    public NfoWriteTrigger Trigger { get; set; }

    public string Content { get; set; } = "";
}
