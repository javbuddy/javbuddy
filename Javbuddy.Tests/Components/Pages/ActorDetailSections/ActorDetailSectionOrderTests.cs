using Javbuddy.Components.Pages.ActorDetailSections;

namespace Javbuddy.Tests.Components.Pages.ActorDetailSections;

public class ActorDetailSectionOrderTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonsense")]
    public void Parse_WithoutAUsableValue_IsTheDefaultOrder(string? value)
    {
        Assert.Equal(
            [ActorDetailSection.Photos, ActorDetailSection.Movies, ActorDetailSection.Scenes, ActorDetailSection.Highlights, ActorDetailSection.Apexes],
            ActorDetailSectionOrder.Parse(value));
    }

    [Fact]
    public void Parse_KeepsTheStoredOrder_AndRoundTripsThroughSerialize()
    {
        IReadOnlyList<ActorDetailSection> order =
            [ActorDetailSection.Apexes, ActorDetailSection.Scenes, ActorDetailSection.Movies, ActorDetailSection.Highlights, ActorDetailSection.Photos];

        Assert.Equal(order, ActorDetailSectionOrder.Parse(ActorDetailSectionOrder.Serialize(order)));
    }

    [Fact]
    public void Parse_DropsUnknownRepeatedAndNumericNames_AndAppendsMissingSectionsInDefaultOrder()
    {
        Assert.Equal(
            [ActorDetailSection.Scenes, ActorDetailSection.Photos, ActorDetailSection.Movies, ActorDetailSection.Highlights, ActorDetailSection.Apexes],
            ActorDetailSectionOrder.Parse(" scenes ,Bogus,Scenes,4,Photos"));
    }

    [Theory]
    [InlineData(1, -1, "Movies,Photos,Scenes,Highlights,Apexes")]
    [InlineData(1, 1, "Photos,Scenes,Movies,Highlights,Apexes")]
    [InlineData(0, -1, "Photos,Movies,Scenes,Highlights,Apexes")]
    [InlineData(4, 1, "Photos,Movies,Scenes,Highlights,Apexes")]
    public void Move_SwapsWithTheNeighbour_AndLeavesTheEndsAlone(int index, int direction, string expected)
    {
        Assert.Equal(expected, ActorDetailSectionOrder.Serialize(ActorDetailSectionOrder.Move(ActorDetailSectionOrder.Default, index, direction)));
    }
}
