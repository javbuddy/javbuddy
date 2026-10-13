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

    [Theory]
    [InlineData("Yua", "Mikami", "Yua Mikami")]
    [InlineData("Madonna", null, "Madonna")]
    [InlineData(null, "Mikami", "Mikami")]
    [InlineData(null, null, "")]
    public void WesternOrder_IsFirstThenLast_Trimmed(string? first, string? last, string expected)
    {
        Assert.Equal(expected, ActorDisplayName.WesternOrder(first, last));
    }

    [Fact]
    public void Actor_WesternOrderName_UsesTheSameOrder()
    {
        var actor = new Javbuddy.Models.Actor { FirstName = "Yua", LastName = "Mikami" };

        Assert.Equal("Yua Mikami", actor.WesternOrderName);
        Assert.Equal("Mikami Yua", actor.DisplayName);
    }
}
