namespace Javbuddy.Models;

/// <summary>What caused a Javbuddy .nfo write — stored with each NfoGeneration it replaced.</summary>
public enum NfoWriteTrigger
{
    ManualEdit,
    ConflictResolution,
    ActorSync,
    MetadataSync,
    DriftPush,
    Restore,
}
