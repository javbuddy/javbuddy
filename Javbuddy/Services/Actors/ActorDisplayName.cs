namespace Javbuddy.Services.Actors;

public static class ActorDisplayName
{
    public static string Format(string firstName, string? lastName) =>
        string.IsNullOrWhiteSpace(lastName) ? firstName : $"{lastName} {firstName}";

    /// <summary>Splits the application's existing LastName FirstName representation for discovery.
    /// A one-name actor has no last-name component.</summary>
    public static (string FirstName, string? LastName) Parse(string displayName)
    {
        var parts = displayName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 1 ? (parts[0], null) : (parts[1], parts[0]);
    }
}
