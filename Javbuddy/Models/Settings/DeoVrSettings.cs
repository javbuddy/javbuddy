namespace Javbuddy.Models;

/// <summary>Single-row settings for the DeoVR integration. Each field can be
/// overridden by its <c>DeoVr__&lt;Field&gt;</c> environment variable (see DeoVrEnvConfig).</summary>
public class DeoVrSettings
{
    public int Id { get; set; }

    /// <summary>Serves the /deovr routes. Off by default: while off they all answer 404.</summary>
    public bool Enabled { get; set; }
}
