using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Movies;
using Microsoft.AspNetCore.Components;

namespace Javbuddy.Tests.Components.Shared;

public class ActorAttributeFiltersTests : BunitContext
{
    private static ActorAttributeOptions Full() => new()
    {
        CupSizes = ["D", "E"],
        Height = new RangeBounds(150, 170),
        Bust = new RangeBounds(80, 100),
        Waist = new RangeBounds(55, 70),
        Hips = new RangeBounds(80, 100),
        Age = new RangeBounds(19, 40),
    };

    [Fact]
    public void RendersOnlyWhatIsOffered()
    {
        var none = Render<ActorAttributeFilters>(p => p
            .Add(x => x.Options, ActorAttributeOptions.None)
            .Add(x => x.Selection, ActorAttributeSelection.Empty));
        Assert.Empty(none.FindAll("button"));

        var some = Render<ActorAttributeFilters>(p => p
            .Add(x => x.Options, new ActorAttributeOptions { Bust = new RangeBounds(80, 100) })
            .Add(x => x.Selection, ActorAttributeSelection.Empty));
        Assert.Contains("Bust", some.Markup);
        Assert.DoesNotContain("Cup Size", some.Markup);
        Assert.DoesNotContain("Waist", some.Markup);
    }

    [Fact]
    public void ShowsAllGroups_WithNarrowedRangesInTheirHeaders()
    {
        var cut = Render<ActorAttributeFilters>(p => p
            .Add(x => x.Options, Full())
            .Add(x => x.Selection, new ActorAttributeSelection { CupSizes = ["E"], Age = new IntRange(25, 30), Hips = new IntRange(85, null) }));

        Assert.Contains("Cup Size (1)", cut.Markup);
        Assert.Contains("Age (25–30 yrs)", cut.Markup);
        Assert.Contains("Hips (85–100 cm)", cut.Markup);
        Assert.Contains("Height", cut.Markup);
        Assert.Contains("Waist", cut.Markup);
        Assert.Contains("Bust", cut.Markup);
    }

    [Fact]
    public void TogglingChips_EmitsTheEditedSelection_AndClearWorks()
    {
        ActorAttributeSelection? emitted = null;
        var cut = Render<ActorAttributeFilters>(p => p
            .Add(x => x.Options, Full())
            .Add(x => x.Selection, new ActorAttributeSelection { CupSizes = ["D"], Age = new IntRange(25, null) })
            .Add(x => x.SelectionChanged, EventCallback.Factory.Create<ActorAttributeSelection>(this, s => emitted = s)));

        cut.FindAll("button").First(b => b.TextContent.StartsWith("Cup Size")).Click();
        cut.FindAll("button").First(b => b.TextContent.Trim() == "E").Click();
        Assert.Equal(["D", "E"], emitted!.CupSizes);
        Assert.Equal(new IntRange(25, null), emitted.Age); // untouched ranges survive

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Clear").Click();
        Assert.Empty(emitted!.CupSizes);
    }

    [Fact]
    public void CommittingARange_EmitsItWithEndStopsAsOpenEnds()
    {
        ActorAttributeSelection? emitted = null;
        var cut = Render<ActorAttributeFilters>(p => p
            .Add(x => x.Options, Full())
            .Add(x => x.Selection, ActorAttributeSelection.Empty)
            .Add(x => x.SelectionChanged, EventCallback.Factory.Create<ActorAttributeSelection>(this, s => emitted = s)));

        cut.FindAll("button").First(b => b.TextContent.StartsWith("Bust")).Click(); // expand the slider
        var thumbs = cut.FindAll("input[type=range]");
        thumbs[0].Change(85);   // min thumb moved off the lower end stop
        Assert.Equal(new IntRange(85, null), emitted!.Bust);

        thumbs = cut.FindAll("input[type=range]");
        thumbs[1].Change(90);   // max thumb moved off the upper end stop
        Assert.Equal(90, emitted!.Bust.Max);
    }
}
