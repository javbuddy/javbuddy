using Bunit;
using Javbuddy.Components.Shared;
using Microsoft.AspNetCore.Components;

namespace Javbuddy.Tests.Components.Shared;

public class ClipRowActionsTests : BunitContext
{
    private IRenderedComponent<ClipRowActions> RenderActions(bool favorite = false, bool busy = false, bool play = false, Action<string>? clicked = null) =>
        Render<ClipRowActions>(p =>
        {
            p.Add(x => x.ClassPrefix, "highlight-editor")
                .Add(x => x.Label, "Big moment")
                .Add(x => x.FavoriteNoun, "highlights")
                .Add(x => x.IsFavorite, favorite)
                .Add(x => x.Busy, busy)
                .Add(x => x.OnToggleFavorite, EventCallback.Factory.Create(this, () => clicked?.Invoke("fav")))
                .Add(x => x.OnEdit, EventCallback.Factory.Create(this, () => clicked?.Invoke("edit")))
                .Add(x => x.OnDelete, EventCallback.Factory.Create(this, () => clicked?.Invoke("delete")));
            if (play)
            {
                p.Add(x => x.OnPlay, EventCallback.Factory.Create(this, () => clicked?.Invoke("play")))
                    .Add(x => x.PlayTitle, "Play just this highlight");
            }
        });

    [Fact]
    public void AlignToTitle_AddsTheFloatingClass_OnlyWhenSet()
    {
        Assert.Empty(RenderActions().FindAll(".clip-row-actions-top"));

        var cut = Render<ClipRowActions>(p => p
            .Add(x => x.ClassPrefix, "scene-editor").Add(x => x.Label, "Scene").Add(x => x.FavoriteNoun, "scenes").Add(x => x.AlignToTitle, true));

        Assert.Equal(3, cut.Find(".clip-row-actions-top").QuerySelectorAll("button").Length);
    }

    [Fact]
    public void RendersFavoriteEditDelete_WithTheHostsClassNamesAndLabels()
    {
        var cut = RenderActions();

        Assert.Equal("Favorite Big moment", cut.Find("button.highlight-editor-fav-btn").GetAttribute("aria-label"));
        Assert.Equal("Add to favorite highlights", cut.Find("button.highlight-editor-fav-btn").GetAttribute("title"));
        Assert.Equal("Edit Big moment", cut.Find("button.highlight-editor-edit-btn").GetAttribute("aria-label"));
        Assert.Equal("Delete Big moment", cut.Find("button.highlight-editor-delete-btn").GetAttribute("aria-label"));
        Assert.All(cut.FindAll("button"), b => Assert.Contains("highlight-editor-icon-btn", b.ClassList));
        Assert.Empty(cut.FindAll(".highlight-editor-play-btn"));
    }

    [Fact]
    public void Favorite_ShowsItsStateOnTheButton()
    {
        var button = RenderActions(favorite: true).Find("button.highlight-editor-fav-btn");

        Assert.Contains("highlight-editor-fav-on", button.ClassList);
        Assert.Equal("true", button.GetAttribute("aria-pressed"));
        Assert.Equal("Unfavorite Big moment", button.GetAttribute("aria-label"));
        Assert.Equal("Remove from favorite highlights", button.GetAttribute("title"));
    }

    [Fact]
    public void PlayButton_ComesFirst_AndOnlyWhenOnPlayIsSet()
    {
        var cut = RenderActions(play: true);

        var buttons = cut.FindAll("button");
        Assert.Equal(4, buttons.Count);
        Assert.Contains("highlight-editor-play-btn", buttons[0].ClassList);
        Assert.Equal("Play Big moment", buttons[0].GetAttribute("aria-label"));
        Assert.Equal("Play just this highlight", buttons[0].GetAttribute("title"));
    }

    [Fact]
    public void Busy_DisablesEverythingButPlay()
    {
        var buttons = RenderActions(busy: true, play: true).FindAll("button");

        Assert.False(buttons[0].HasAttribute("disabled"));
        Assert.All(buttons.Skip(1), b => Assert.True(b.HasAttribute("disabled")));
    }

    [Fact]
    public void Clicks_RaiseTheirCallbacks()
    {
        var clicked = new List<string>();
        var cut = RenderActions(play: true, clicked: clicked.Add);

        foreach (var button in cut.FindAll("button")) button.Click();

        Assert.Equal(["play", "fav", "edit", "delete"], clicked);
    }
}
