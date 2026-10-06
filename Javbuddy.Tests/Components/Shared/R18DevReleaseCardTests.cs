using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.R18Dev;
using Microsoft.AspNetCore.Components;

namespace Javbuddy.Tests.Components.Shared;

public class R18DevReleaseCardTests : BunitContext
{
    private static R18DevReleaseRow Row(string statusClass, string label = "2020-01-02", string dvdId = "MIDE-001") =>
        new(new R18DevFilmographyEntry(dvdId, "A Title", null, new DateOnly(2020, 1, 2), null), statusClass, label, $"/movies/{dvdId}", dvdId);

    [Fact]
    public void RendersCodeStatusClassLabelAndLink()
    {
        var cut = Render<R18DevReleaseCard>(p => p.Add(x => x.Release, Row("got", "Got")));

        Assert.Contains("MIDE-001", cut.Find(".poster-title").TextContent);
        Assert.Equal("Got", cut.Find(".poster-meta").TextContent.Trim());
        Assert.Equal("/movies/MIDE-001", cut.Find("a.poster-card").GetAttribute("href"));
        Assert.NotNull(cut.Find(".poster-image.status-got"));
    }

    [Fact]
    public void InLibraryRelease_HasNoQuickActions()
    {
        var cut = Render<R18DevReleaseCard>(p => p
            .Add(x => x.Release, Row("wanted", "Missing"))
            .Add(x => x.OnSearch, EventCallback.Factory.Create<string>(this, _ => { }))
            .Add(x => x.OnAdd, EventCallback.Factory.Create<string>(this, _ => { })));

        Assert.Empty(cut.FindAll("button"));
    }

    [Fact]
    public void MissingRelease_SearchButtonRaisesOnSearchWithTheCode()
    {
        string? searched = null;
        var cut = Render<R18DevReleaseCard>(p => p
            .Add(x => x.Release, Row("missing"))
            .Add(x => x.OnSearch, EventCallback.Factory.Create<string>(this, code => searched = code)));

        cut.Find("button.missing-poster-search").Click();

        Assert.Equal("MIDE-001", searched);
    }

    [Fact]
    public void AddButton_OnlyRenderedWhenOnAddIsProvided_AndRaisesItWithTheCode()
    {
        var withoutHandler = Render<R18DevReleaseCard>(p => p.Add(x => x.Release, Row("missing")));
        Assert.Empty(withoutHandler.FindAll("button.missing-poster-add"));

        string? added = null;
        var cut = Render<R18DevReleaseCard>(p => p
            .Add(x => x.Release, Row("missing"))
            .Add(x => x.OnAdd, EventCallback.Factory.Create<string>(this, code => added = code)));

        cut.Find("button.missing-poster-add").Click();

        Assert.Equal("MIDE-001", added);
    }

    [Fact]
    public void AddInProgress_DisablesTheAddButton()
    {
        var cut = Render<R18DevReleaseCard>(p => p
            .Add(x => x.Release, Row("missing"))
            .Add(x => x.OnAdd, EventCallback.Factory.Create<string>(this, _ => { }))
            .Add(x => x.AddInProgress, true));

        Assert.True(cut.Find("button.missing-poster-add").HasAttribute("disabled"));
    }
}
