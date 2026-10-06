using Javbuddy.Services.Actors;

namespace Javbuddy.Tests.Services.Actors;

public class ActorDisplayNameTests
{
    [Theory]
    [InlineData("Yua", "Mikami", "Mikami Yua")]
    [InlineData("Madonna", null, "Madonna")]
    public void Format_UsesLastNameFirst_AndOmitsMissingLastName(string firstName, string? lastName, string expected)
    {
        Assert.Equal(expected, ActorDisplayName.Format(firstName, lastName));
    }

    [Theory]
    [InlineData("Mikami Yua", "Yua", "Mikami")]
    [InlineData("Madonna", "Madonna", null)]
    public void Parse_SplitsExistingDisplayName(string displayName, string expectedFirstName, string? expectedLastName)
    {
        var result = ActorDisplayName.Parse(displayName);

        Assert.Equal(expectedFirstName, result.FirstName);
        Assert.Equal(expectedLastName, result.LastName);
    }
}
