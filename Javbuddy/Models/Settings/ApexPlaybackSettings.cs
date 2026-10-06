namespace Javbuddy.Models;

/// <summary>Single-row settings for how long an apex plays: the default lead-in before
/// it and tail after it, which an apex's own MovieApex.LeadInSeconds/TailSeconds override. Each field
/// can be overridden by an <c>ApexPlayback__&lt;Field&gt;</c> environment variable (see
/// ApexPlaybackEnvConfig).</summary>
public class ApexPlaybackSettings
{
    public int Id { get; set; }

    public double LeadInSeconds { get; set; } = 5;

    public double TailSeconds { get; set; } = 5;
}
