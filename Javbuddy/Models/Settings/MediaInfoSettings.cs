namespace Javbuddy.Models;

/// <summary>Single-row settings for the MediaInfo technical probe of local video files. Enabled by
/// default — unlike the URL/API-key integrations, this needs no external service and ships its own
/// native library via the MediaInfo.Wrapper.Core NuGet package, so there's nothing to configure
/// before it's useful; the toggle exists only for a user who wants to turn it off.</summary>
public class MediaInfoSettings
{
    public int Id { get; set; }

    public bool Enabled { get; set; } = true;
}
