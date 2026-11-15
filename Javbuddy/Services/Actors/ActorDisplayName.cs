namespace Javbuddy.Services.Actors;

public static class ActorDisplayName
{
    public static string Format(string firstName, string? lastName) =>
        string.IsNullOrWhiteSpace(lastName) ? firstName : $"{lastName} {firstName}";

    /// <summary>A placeholder's label as it is stored: a "/" or "\" (which would split the actor's page route) becomes a
    /// "-", and it is cut to 100 characters. Null when nothing is left.</summary>
    public static string? SanitizePlaceholderLabel(string? label)
    {
        var cleaned = label?.Replace('/', '-').Replace('\\', '-').Trim();
        if (string.IsNullOrEmpty(cleaned)) return null;
        return cleaned.Length > 100 ? cleaned[..100].TrimEnd() : cleaned;
    }

    /// <summary>The name to show for an actor read as plain columns (a projection that doesn't build an Actor):
    /// a placeholder's label with its movie code in brackets, anyone else's "Last First".</summary>
    public static string For(string? firstName, string? lastName, int? placeholderMovieId) =>
        placeholderMovieId is null ? Format(firstName ?? string.Empty, lastName) : Placeholder(firstName ?? string.Empty, lastName);

    /// <summary>An unidentified placeholder's name: its label with the movie's code, which is stored as the last name,
    /// shown in brackets. The brackets are only ever added here, never part of the stored name.</summary>
    public static string Placeholder(string label, string? movieCode) =>
        string.IsNullOrWhiteSpace(movieCode) ? label : $"{label} ({movieCode})";

    /// <summary>"First Last" (trimmed; empty when neither is set): the Western order sources that list an actor
    /// that way credit her under, as opposed to <see cref="Format"/>'s "Last First".</summary>
    public static string WesternOrder(string? firstName, string? lastName) => $"{firstName} {lastName}".Trim();

    /// <summary>Splits the application's existing LastName FirstName representation for discovery.
    /// A one-name actor has no last-name component.</summary>
    public static (string FirstName, string? LastName) Parse(string displayName)
    {
        var parts = displayName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 1 ? (parts[0], null) : (parts[1], parts[0]);
    }
}
