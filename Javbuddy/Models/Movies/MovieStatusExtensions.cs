namespace Javbuddy.Models;

public static class MovieStatusExtensions
{
    // Deliberately not derived from the enum member name (e.g. status.ToString().ToLowerInvariant()):
    // "missing"/"status-missing" is already used by ActorMissing.razor as the sentinel for "not
    // tracked in this library at all" (a concept fully independent of MovieStatus). A tracked movie
    // keeps its pre-existing "wanted"/"status-wanted" class/color for the Missing status instead of
    // colliding with that sentinel. Got is unaffected since that member was never renamed.
    public static string ToPosterStatusSuffix(this MovieStatus status) => status switch
    {
        MovieStatus.Got => "got",
        _ => "wanted",
    };

    public static string ToPosterCssClass(this MovieStatus status) => $"status-{status.ToPosterStatusSuffix()}";
}
