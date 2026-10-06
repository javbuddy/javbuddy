using Bunit;
using Javbuddy.Components.Shared;
using Microsoft.AspNetCore.Components.Web;

namespace Javbuddy.Tests.Components.Shared;

public class MovieGalleryTests : BunitContext
{
    public MovieGalleryTests() => JSInterop.SetupModule("./Components/Shared/ImageZoomControls.razor.js").Mode = JSRuntimeMode.Loose;

    private static readonly string[] Urls = ["a.jpg", "b.jpg", "c.jpg"];

    private IRenderedComponent<MovieGallery> RenderOpenGallery()
    {
        var cut = Render<MovieGallery>(p => p.Add(x => x.ThumbUrls, Urls));
        cut.FindAll(".fanart-thumb")[0].Click();
        return cut;
    }

    private static void Swipe(IRenderedComponent<MovieGallery> cut, double startX, double endX)
    {
        var image = cut.Find(".gallery-image");
        image.TouchStart(new TouchEventArgs { Touches = [new TouchPoint { ClientX = startX, ClientY = 100 }] });
        image.TouchEnd(new TouchEventArgs { ChangedTouches = [new TouchPoint { ClientX = endX, ClientY = 100 }] });
    }

    [Fact]
    public void DoesNotRenderLeakedCSharpSourceAsMarkup()
    {
        // Regression test: a duplicate OnParametersSet once sat outside the
        // @code block, which Razor rendered as literal text at the end of the component's markup.
        var cut = Render<MovieGallery>(p => p.Add(x => x.ThumbUrls, Urls));

        Assert.DoesNotContain("protected override", cut.Markup);
        Assert.DoesNotContain("OnParametersSet", cut.Markup);
    }

    [Fact]
    public void SwipingLeftAdvancesToTheNextImage()
    {
        var cut = RenderOpenGallery();

        Swipe(cut, startX: 200, endX: 100);

        Assert.Equal("2 / 3", cut.Find(".gallery-index").TextContent);
    }

    [Fact]
    public void SwipingRightGoesToThePreviousImage()
    {
        var cut = RenderOpenGallery();
        cut.Find(".gallery-nav-next").Click();

        Swipe(cut, startX: 100, endX: 200);

        Assert.Equal("1 / 3", cut.Find(".gallery-index").TextContent);
    }

    [Fact]
    public void ASwipeBelowTheThresholdDoesNotNavigate()
    {
        var cut = RenderOpenGallery();

        Swipe(cut, startX: 200, endX: 180);

        Assert.Equal("1 / 3", cut.Find(".gallery-index").TextContent);
    }

    [Fact]
    public void SwipingAtTheLastImageWrapsAroundToTheFirst()
    {
        var cut = RenderOpenGallery();
        cut.Find(".gallery-nav-next").Click();
        cut.Find(".gallery-nav-next").Click();

        Swipe(cut, startX: 200, endX: 100);

        Assert.Equal("1 / 3", cut.Find(".gallery-index").TextContent);
    }

    [Fact]
    public void LargeGallery_GroupsPicturesIntoACompactProgressBar()
    {
        var urls = Enumerable.Range(1, 599).Select(index => $"{index}.jpg").ToArray();
        var cut = Render<MovieGallery>(p => p.Add(x => x.ThumbUrls, urls));

        cut.FindAll(".fanart-thumb")[371].Click();

        Assert.Equal(100, cut.FindAll(".gallery-progress-segment").Count);
        Assert.Equal("Go to images 372 to 377 of 599", cut.FindAll(".gallery-progress-segment")[62].GetAttribute("aria-label"));

        cut.FindAll(".gallery-progress-segment")[62].Click();

        Assert.Equal("372 / 599", cut.Find(".gallery-index").TextContent);
        Assert.Contains("is-seen", cut.FindAll(".gallery-progress-segment")[62].GetAttribute("class"));
        Assert.Contains("is-current", cut.FindAll(".gallery-progress-segment")[62].GetAttribute("class"));
        Assert.DoesNotContain("is-current", cut.FindAll(".gallery-progress-segment")[61].GetAttribute("class"));
    }

    [Fact]
    public void ThumbOverlay_RendersPerThumbnailIndex_WhenSupplied()
    {
        var cut = Render<MovieGallery>(p => p
            .Add(x => x.ThumbUrls, Urls)
            .Add(x => x.ThumbOverlay, idx => builder =>
            {
                builder.OpenElement(0, "button");
                builder.AddAttribute(1, "class", "overlay-marker");
                builder.AddContent(2, $"overlay-{idx}");
                builder.CloseElement();
            }));

        var overlays = cut.FindAll(".fanart-thumb-overlay");
        Assert.Equal(3, overlays.Count);

        var markers = cut.FindAll(".overlay-marker");
        Assert.Equal(3, markers.Count);
        Assert.Equal("overlay-0", markers[0].TextContent);
        Assert.Equal("overlay-1", markers[1].TextContent);
        Assert.Equal("overlay-2", markers[2].TextContent);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ThumbBadge_RendersPerThumbnailIndex_WhenSupplied(bool cardWrapper)
    {
        // CardWrapper=false is what TorrentSort.razor actually uses — both branches render the
        // badge independently in MovieGallery.razor, and a prior bug left the false branch's
        // ThumbBadge call out entirely, so both must be covered here, not just the default.
        var cut = Render<MovieGallery>(p => p
            .Add(x => x.ThumbUrls, Urls)
            .Add(x => x.CardWrapper, cardWrapper)
            .Add(x => x.ThumbBadge, idx => builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "class", "badge-marker");
                builder.AddContent(2, $"badge-{idx}");
                builder.CloseElement();
            }));

        var badges = cut.FindAll(".badge-marker");
        Assert.Equal(3, badges.Count);
        Assert.Equal("badge-0", badges[0].TextContent);
        Assert.Equal("badge-1", badges[1].TextContent);
        Assert.Equal("badge-2", badges[2].TextContent);

        // Badges aren't hover-gated — no .fanart-thumb-overlay wrapper for badge-only content.
        Assert.Empty(cut.FindAll(".fanart-thumb-overlay"));
    }

    [Fact]
    public void OmittingThumbOverlayAndThumbBadge_RendersNoOverlayOrBadgeMarkup()
    {
        var cut = Render<MovieGallery>(p => p.Add(x => x.ThumbUrls, Urls));

        Assert.Empty(cut.FindAll(".fanart-thumb-overlay"));
        Assert.Equal(3, cut.FindAll(".fanart-thumb-cell").Count);
        Assert.Equal(3, cut.FindAll(".fanart-thumb").Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Lazy_RendersDataSrcInsteadOfNativeLazyAttribute(bool cardWrapper)
    {
        var cut = Render<MovieGallery>(p => p
            .Add(x => x.ThumbUrls, Urls)
            .Add(x => x.CardWrapper, cardWrapper)
            .Add(x => x.Lazy, true));

        var thumbs = cut.FindAll(".fanart-thumb");
        Assert.Equal(3, thumbs.Count);
        foreach (var thumb in thumbs)
        {
            Assert.Null(thumb.GetAttribute("src"));
            Assert.Null(thumb.GetAttribute("loading"));
            Assert.NotNull(thumb.GetAttribute("data-src"));
            Assert.Contains("js-lazy-gallery-img", thumb.GetAttribute("class"));
        }
    }

    [Fact]
    public void OriginalToggle_IsHidden_WhenOriginalUrlsNotSupplied()
    {
        var cut = RenderOpenGallery();

        Assert.Empty(cut.FindAll(".gallery-original-toggle"));
    }

    [Fact]
    public void OriginalToggle_SwitchesTheImageSrc_AndReverts_OnSecondClick()
    {
        var originalUrls = new[] { "a-original.jpg", "b-original.jpg", "c-original.jpg" };
        var cut = Render<MovieGallery>(p => p
            .Add(x => x.ThumbUrls, Urls)
            .Add(x => x.OriginalUrls, originalUrls));
        cut.FindAll(".fanart-thumb")[0].Click();

        var toggle = cut.Find(".gallery-original-toggle");
        Assert.Equal("false", toggle.GetAttribute("aria-pressed"));
        Assert.Equal(Urls[0], cut.Find(".gallery-image").GetAttribute("src"));

        toggle.Click();

        Assert.Equal("true", cut.Find(".gallery-original-toggle").GetAttribute("aria-pressed"));
        Assert.Equal(originalUrls[0], cut.Find(".gallery-image").GetAttribute("src"));

        cut.Find(".gallery-original-toggle").Click();

        Assert.Equal(Urls[0], cut.Find(".gallery-image").GetAttribute("src"));
    }

    [Fact]
    public void OriginalToggle_PersistsAcrossNavigation_UntilToggledOff()
    {
        var originalUrls = new[] { "a-original.jpg", "b-original.jpg", "c-original.jpg" };
        var cut = Render<MovieGallery>(p => p
            .Add(x => x.ThumbUrls, Urls)
            .Add(x => x.OriginalUrls, originalUrls));
        cut.FindAll(".fanart-thumb")[0].Click();
        cut.Find(".gallery-original-toggle").Click();
        Assert.Equal(originalUrls[0], cut.Find(".gallery-image").GetAttribute("src"));

        cut.Find(".gallery-nav-next").Click();

        Assert.Equal(originalUrls[1], cut.Find(".gallery-image").GetAttribute("src"));
        Assert.Equal("true", cut.Find(".gallery-original-toggle").GetAttribute("aria-pressed"));

        cut.Find(".gallery-nav-prev").Click();

        Assert.Equal(originalUrls[0], cut.Find(".gallery-image").GetAttribute("src"));
        Assert.Equal("true", cut.Find(".gallery-original-toggle").GetAttribute("aria-pressed"));

        cut.Find(".gallery-original-toggle").Click();

        Assert.Equal(Urls[0], cut.Find(".gallery-image").GetAttribute("src"));
        cut.Find(".gallery-nav-next").Click();
        Assert.Equal(Urls[1], cut.Find(".gallery-image").GetAttribute("src"));
        Assert.Equal("false", cut.Find(".gallery-original-toggle").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void OriginalToggle_ResetsToTheWebPPreview_WhenTheLightboxIsClosedAndReopened()
    {
        var originalUrls = new[] { "a-original.jpg", "b-original.jpg", "c-original.jpg" };
        var cut = Render<MovieGallery>(p => p
            .Add(x => x.ThumbUrls, Urls)
            .Add(x => x.OriginalUrls, originalUrls));
        cut.FindAll(".fanart-thumb")[0].Click();
        cut.Find(".gallery-original-toggle").Click();
        Assert.Equal("true", cut.Find(".gallery-original-toggle").GetAttribute("aria-pressed"));

        cut.Find(".gallery-close-btn").Click();
        cut.FindAll(".fanart-thumb")[0].Click();

        Assert.Equal(Urls[0], cut.Find(".gallery-image").GetAttribute("src"));
        Assert.Equal("false", cut.Find(".gallery-original-toggle").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void OriginalToggle_ShowsLoadingSpinner_UntilTheOriginalImageLoads()
    {
        var originalUrls = new[] { "a-original.jpg", "b-original.jpg", "c-original.jpg" };
        var cut = Render<MovieGallery>(p => p
            .Add(x => x.ThumbUrls, Urls)
            .Add(x => x.OriginalUrls, originalUrls));
        cut.FindAll(".fanart-thumb")[0].Click();

        cut.Find(".gallery-original-toggle").Click();
        Assert.Single(cut.FindAll(".gallery-image-loading"));

        cut.Find(".gallery-image").TriggerEvent("onload", new ProgressEventArgs());

        Assert.Empty(cut.FindAll(".gallery-image-loading"));
    }

    [Fact]
    public void LightboxActions_RenderForTheOpenImage_WhenSupplied()
    {
        var cut = Render<MovieGallery>(p => p
            .Add(x => x.ThumbUrls, Urls)
            .Add(x => x.LightboxActions, idx => builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "class", "lightbox-action-marker");
                builder.AddContent(2, $"action-{idx}");
                builder.CloseElement();
            }));

        cut.FindAll(".fanart-thumb")[1].Click();

        Assert.Equal("action-1", cut.Find(".lightbox-action-marker").TextContent);
    }

    [Fact]
    public void OpenRequestKey_OpensTheGalleryWithoutRenderingThumbnails()
    {
        var cut = Render<MovieGallery>(p => p
            .Add(x => x.ThumbUrls, Urls)
            .Add(x => x.ShowThumbnails, false)
            .Add(x => x.OpenRequestKey, "candidate-1"));

        Assert.Empty(cut.FindAll(".fanart-thumb"));
        Assert.Equal("1 / 3", cut.Find(".gallery-index").TextContent);
        Assert.Equal("a.jpg", cut.Find(".gallery-image").GetAttribute("src"));
    }

    [Fact]
    public void ShrinkingWhileOpenOnTheLastImage_ShowsTheNewLastImage()
    {
        // Movie Detail deletes images while the viewer is open.
        var cut = Render<MovieGallery>(p => p.Add(x => x.ThumbUrls, Urls));
        cut.FindAll(".fanart-thumb")[2].Click();

        cut.Render(p => p.Add(x => x.ThumbUrls, new[] { "a.jpg", "b.jpg" }));

        Assert.Equal("b.jpg", cut.Find(".gallery-image").GetAttribute("src"));
        Assert.Equal("2 / 2", cut.Find(".gallery-index").TextContent);
    }

    [Fact]
    public void ShrinkingToEmptyWhileOpen_ClosesTheViewer()
    {
        var closed = 0;
        var cut = Render<MovieGallery>(p => p.Add(x => x.ThumbUrls, new[] { "a.jpg" }).Add(x => x.OnClose, () => closed++));
        cut.Find(".fanart-thumb").Click();

        cut.Render(p => p.Add(x => x.ThumbUrls, Array.Empty<string>()));

        Assert.Empty(cut.FindAll(".gallery-lightbox"));
        Assert.Equal(1, closed);
    }

    [Fact]
    public void LightboxCovered_IsPassedToTheOpenViewer()
    {
        var cut = Render<MovieGallery>(p => p.Add(x => x.ThumbUrls, Urls).Add(x => x.LightboxCovered, true));
        cut.FindAll(".fanart-thumb")[0].Click();

        Assert.True(cut.FindComponent<GalleryLightbox>().Instance.Covered);
    }
}
