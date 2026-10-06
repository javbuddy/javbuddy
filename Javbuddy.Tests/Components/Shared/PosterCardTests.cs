using Bunit;
using Javbuddy.Components.Shared;

namespace Javbuddy.Tests.Components.Shared;

public class PosterCardTests : BunitContext
{
    [Fact]
    public void EagerLoading_RendersPlainImgWithSrc()
    {
        var cut = Render<PosterCard>(p => p
            .Add(x => x.Href, "/movies/ABC-123")
            .Add(x => x.StatusCssClass, "status-wanted")
            .Add(x => x.ImageUrl, "/image-cache/ABC-123/poster/thumb")
            .Add(x => x.Loading, PosterImageLoading.Eager)
            .Add(x => x.PlaceholderText, "ABC-123")
            .Add(x => x.TitleText, "ABC-123"));

        var img = cut.Find("img");
        Assert.Equal("/image-cache/ABC-123/poster/thumb", img.GetAttribute("src"));
        Assert.Null(img.GetAttribute("data-src"));
        Assert.Null(img.GetAttribute("loading"));
    }

    [Fact]
    public void NativeLazyLoading_RendersSrcWithLoadingLazyAttribute()
    {
        var cut = Render<PosterCard>(p => p
            .Add(x => x.Href, "/x")
            .Add(x => x.StatusCssClass, "status-missing")
            .Add(x => x.ImageUrl, "https://example.test/poster.jpg")
            .Add(x => x.Loading, PosterImageLoading.NativeLazy)
            .Add(x => x.PlaceholderText, "X")
            .Add(x => x.TitleText, "X"));

        var img = cut.Find("img");
        Assert.Equal("https://example.test/poster.jpg", img.GetAttribute("src"));
        Assert.Equal("lazy", img.GetAttribute("loading"));
    }

    [Fact]
    public void JsLazyLoading_RendersDataSrcWithNoSrcOrLoadingAttribute()
    {
        var cut = Render<PosterCard>(p => p
            .Add(x => x.Href, "/x")
            .Add(x => x.StatusCssClass, "status-missing")
            .Add(x => x.ImageUrl, "https://example.test/poster.jpg")
            .Add(x => x.Loading, PosterImageLoading.JsLazy)
            .Add(x => x.PlaceholderText, "X")
            .Add(x => x.TitleText, "X"));

        var img = cut.Find("img");
        Assert.Equal("https://example.test/poster.jpg", img.GetAttribute("data-src"));
        Assert.Null(img.GetAttribute("src"));
        Assert.Null(img.GetAttribute("loading"));
        Assert.Contains("js-lazy-poster-img", img.GetAttribute("class"));
    }

    [Fact]
    public void NoImageUrl_RendersPlaceholder()
    {
        var cut = Render<PosterCard>(p => p
            .Add(x => x.Href, "/x")
            .Add(x => x.StatusCssClass, "status-wanted")
            .Add(x => x.ImageUrl, (string?)null)
            .Add(x => x.PlaceholderText, "ABC-123")
            .Add(x => x.TitleText, "ABC-123"));

        Assert.Empty(cut.FindAll("img"));
        Assert.Equal("ABC-123", cut.Find(".poster-placeholder").TextContent);
    }

    [Fact]
    public void StatusCssClass_AppliedToImageDiv()
    {
        var cut = Render<PosterCard>(p => p
            .Add(x => x.Href, "/x")
            .Add(x => x.StatusCssClass, "status-downloading")
            .Add(x => x.PlaceholderText, "X")
            .Add(x => x.TitleText, "X"));

        Assert.Contains("status-downloading", cut.Find(".poster-image").GetAttribute("class"));
    }

    [Fact]
    public void MetaSubLines_NullOrEmptyLine_RendersNonBreakingSpace()
    {
        var cut = Render<PosterCard>(p => p
            .Add(x => x.Href, "/x")
            .Add(x => x.StatusCssClass, "status-wanted")
            .Add(x => x.PlaceholderText, "X")
            .Add(x => x.TitleText, "X")
            .Add(x => x.MetaSubLines, new string?[] { "2026", null }));

        var subLines = cut.FindAll(".poster-meta-sub");
        Assert.Equal(2, subLines.Count);
        Assert.Equal("2026", subLines[0].TextContent);
        Assert.Equal(" ", subLines[1].TextContent);
    }

    [Fact]
    public void TitleTooltip_SetsTitleAttribute()
    {
        var cut = Render<PosterCard>(p => p
            .Add(x => x.Href, "/x")
            .Add(x => x.StatusCssClass, "status-wanted")
            .Add(x => x.PlaceholderText, "X")
            .Add(x => x.TitleText, "ABC-123")
            .Add(x => x.TitleTooltip, "Full Display Name"));

        Assert.Equal("Full Display Name", cut.Find(".poster-title").GetAttribute("title"));
    }

    [Fact]
    public void NoIsFavorite_RendersNoFavoriteToggle()
    {
        var cut = Render<PosterCard>(p => p
            .Add(x => x.Href, "/x")
            .Add(x => x.StatusCssClass, "status-missing")
            .Add(x => x.PlaceholderText, "X")
            .Add(x => x.TitleText, "X"));

        Assert.Empty(cut.FindAll(".poster-favorite"));
    }

    [Fact]
    public void IsFavorite_RendersToggleOutsideTheLink_AndClickInvokesCallback()
    {
        var clicks = 0;
        var cut = Render<PosterCard>(p => p
            .Add(x => x.Href, "/movies/ABC-123")
            .Add(x => x.StatusCssClass, "status-got")
            .Add(x => x.PlaceholderText, "ABC-123")
            .Add(x => x.TitleText, "ABC-123")
            .Add(x => x.IsFavorite, true)
            .Add(x => x.OnToggleFavorite, () => clicks++));

        var toggle = cut.Find("button.poster-favorite");
        Assert.Contains("active", toggle.GetAttribute("class"));
        Assert.Equal("Unfavorite ABC-123", toggle.GetAttribute("aria-label"));
        Assert.Null(toggle.Closest("a"));

        toggle.Click();

        Assert.Equal(1, clicks);
    }

    [Fact]
    public void CornerBadge_RendersWithItsTooltip_OnlyWhenSet()
    {
        var withBadge = Render<PosterCard>(p => p
            .Add(c => c.Href, "/movie/1")
            .Add(c => c.StatusCssClass, "status-got")
            .Add(c => c.PlaceholderText, "No image")
            .Add(c => c.TitleText, "SIVR-059")
            .Add(c => c.CornerBadge, "VR")
            .Add(c => c.CornerBadgeTooltip, "VR180 SBS"));
        var plain = Render<PosterCard>(p => p
            .Add(c => c.Href, "/movie/2")
            .Add(c => c.StatusCssClass, "status-got")
            .Add(c => c.PlaceholderText, "No image")
            .Add(c => c.TitleText, "MIDE-400"));

        var badge = withBadge.Find(".poster-image .poster-corner-badge");
        Assert.Equal("VR", badge.TextContent);
        Assert.Equal("VR180 SBS", badge.GetAttribute("title"));
        Assert.Empty(plain.FindAll(".poster-corner-badge"));
    }
}
