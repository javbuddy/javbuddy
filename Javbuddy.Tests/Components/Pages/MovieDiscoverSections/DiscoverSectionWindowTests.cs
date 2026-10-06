using Javbuddy.Components.Pages.MovieDiscoverSections;

namespace Javbuddy.Tests.Components.Pages.MovieDiscoverSections;

public class DiscoverSectionWindowTests
{
    [Theory]
    [InlineData(40, 0, 12)]
    [InlineData(5, 0, 5)]
    [InlineData(0, 0, 0)]
    public void Initial_IsTheFirstFewCandidates(int total, int expectedStart, int expectedCount)
    {
        Assert.Equal(new DiscoverSectionWindow(expectedStart, expectedCount), DiscoverSectionWindow.Initial(total));
    }

    [Fact]
    public void Clamp_RangeInsideTheSection_IsKeptAsIs()
    {
        Assert.Equal(new DiscoverSectionWindow(12, 18), DiscoverSectionWindow.Clamp(12, 18, 6, 40));
    }

    [Fact]
    public void Clamp_RangeRunningPastTheEnd_StopsAtTheLastCandidate()
    {
        Assert.Equal(new DiscoverSectionWindow(36, 4), DiscoverSectionWindow.Clamp(36, 18, 6, 40));
    }

    [Fact]
    public void Clamp_SectionScrolledFarAbove_KeepsItsLastRow()
    {
        // A section far above the viewport asks for a start beyond its end.
        Assert.Equal(new DiscoverSectionWindow(36, 4), DiscoverSectionWindow.Clamp(120, 30, 6, 40));
    }

    [Fact]
    public void Clamp_SectionFarBelow_KeepsItsFirstRow()
    {
        // A section far below the viewport asks for a negative count.
        Assert.Equal(new DiscoverSectionWindow(0, 6), DiscoverSectionWindow.Clamp(0, -42, 6, 40));
    }

    [Fact]
    public void Clamp_StartMidRow_AlignsDownToTheRow()
    {
        Assert.Equal(new DiscoverSectionWindow(12, 12), DiscoverSectionWindow.Clamp(14, 12, 6, 40));
    }

    [Fact]
    public void Clamp_HugeCount_IsCappedAtTheBackstop()
    {
        Assert.Equal(new DiscoverSectionWindow(0, DiscoverSectionWindow.MaxItems), DiscoverSectionWindow.Clamp(0, int.MaxValue, 2, 10_000));
    }

    [Fact]
    public void Clamp_EmptySection_IsEmpty()
    {
        Assert.Equal(new DiscoverSectionWindow(0, 0), DiscoverSectionWindow.Clamp(5, 10, 3, 0));
    }

    [Fact]
    public void Clamp_ZeroColumns_IsTreatedAsOne()
    {
        Assert.Equal(new DiscoverSectionWindow(3, 1), DiscoverSectionWindow.Clamp(3, 0, 0, 10));
    }
}
