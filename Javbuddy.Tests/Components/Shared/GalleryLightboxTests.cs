using Bunit;
using Javbuddy.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Javbuddy.Tests.Components.Shared;

public class GalleryLightboxTests : BunitContext
{
    private readonly BunitJSModuleInterop zoomModule;

    public GalleryLightboxTests()
    {
        zoomModule = JSInterop.SetupModule("./Components/Shared/ImageZoomControls.razor.js");
        zoomModule.Mode = JSRuntimeMode.Loose;
    }

    private int closeCount;

    // Mirrors a caller's @bind-Index: navigation only raises IndexChanged, so the new index is fed
    // back in as a parameter, exactly as a parent re-render would.
    private IRenderedComponent<GalleryLightbox> RenderLightbox(
        int count = 3,
        int index = 0,
        Func<int, string?>? originalUrl = null,
        Action<ComponentParameterCollectionBuilder<GalleryLightbox>>? configure = null)
    {
        IRenderedComponent<GalleryLightbox>? cut = null;
        cut = Render<GalleryLightbox>(p =>
        {
            p.Add(x => x.Count, count)
                .Add(x => x.Index, index)
                .Add(x => x.IndexChanged, EventCallback.Factory.Create<int>(this, i => cut!.Render(r => r.Add(x => x.Index, i))))
                .Add(x => x.ImageUrl, i => $"full-{i}.jpg")
                .Add(x => x.OnClose, EventCallback.Factory.Create(this, () => closeCount++));
            if (originalUrl is not null)
            {
                p.Add(x => x.OriginalUrl, originalUrl);
            }
            configure?.Invoke(p);
        });
        return cut;
    }

    private static string Src(IRenderedComponent<GalleryLightbox> cut) =>
        cut.Find(".gallery-image").GetAttribute("src")!;

    private static void Swipe(IRenderedComponent<GalleryLightbox> cut, double startX, double endX, double startY = 100, double endY = 100)
    {
        var image = cut.Find(".gallery-image");
        image.TouchStart(new TouchEventArgs { Touches = [new TouchPoint { ClientX = startX, ClientY = startY }] });
        image.TouchEnd(new TouchEventArgs { ChangedTouches = [new TouchPoint { ClientX = endX, ClientY = endY }] });
    }

    [Fact]
    public void ShowsTheImageAtTheBoundIndex()
    {
        var cut = RenderLightbox(index: 1);

        Assert.Equal("full-1.jpg", Src(cut));
        Assert.Equal("2 / 3", cut.Find(".gallery-index").TextContent);
        Assert.Equal("Image 2", cut.Find(".gallery-image").GetAttribute("alt"));
    }

    [Fact]
    public void PrevAndNext_WrapAround()
    {
        var cut = RenderLightbox();

        cut.Find(".gallery-nav-prev").Click();
        Assert.Equal("full-2.jpg", Src(cut));

        cut.Find(".gallery-nav-next").Click();
        Assert.Equal("full-0.jpg", Src(cut));
    }

    [Fact]
    public void ArrowKeysNavigate_AndEscapeCloses()
    {
        var cut = RenderLightbox();
        var lightbox = cut.Find(".gallery-lightbox");

        lightbox.KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        Assert.Equal("full-1.jpg", Src(cut));

        cut.Find(".gallery-lightbox").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        Assert.Equal("full-0.jpg", Src(cut));

        cut.Find(".gallery-lightbox").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.Equal(1, closeCount);
    }

    [Fact]
    public void CloseButtonAndBackdrop_RaiseOnClose()
    {
        var cut = RenderLightbox();

        cut.Find(".gallery-close-btn").Click();
        cut.Find(".gallery-lightbox").Click();

        Assert.Equal(2, closeCount);
    }

    [Fact]
    public void HorizontalSwipesNavigate_ButMostlyVerticalOnesDoNot()
    {
        var cut = RenderLightbox();

        Swipe(cut, startX: 200, endX: 100);
        Assert.Equal("full-1.jpg", Src(cut));

        Swipe(cut, startX: 100, endX: 200);
        Assert.Equal("full-0.jpg", Src(cut));

        Swipe(cut, startX: 200, endX: 130, startY: 100, endY: 300);
        Assert.Equal("full-0.jpg", Src(cut));
    }

    [Fact]
    public void NavIsHidden_ForASingleImage()
    {
        var cut = RenderLightbox(count: 1);

        Assert.Empty(cut.FindAll(".gallery-nav"));
    }

    [Fact]
    public void Swipes_DoNotNavigate_WhileZoomedIn()
    {
        var cut = RenderLightbox();
        var zoom = cut.FindComponent<ImageZoomControls>();

        cut.InvokeAsync(() => zoom.Instance.SetZoomed(true));
        Swipe(cut, startX: 200, endX: 100);
        Assert.Equal("full-0.jpg", Src(cut));

        cut.InvokeAsync(() => zoom.Instance.SetZoomed(false));
        Swipe(cut, startX: 200, endX: 100);
        Assert.Equal("full-1.jpg", Src(cut));
    }

    [Fact]
    public void ASecondFinger_TurnsTheGestureIntoAPinch_NotASwipe()
    {
        var cut = RenderLightbox();
        var image = cut.Find(".gallery-image");

        image.TouchStart(new TouchEventArgs { Touches = [new TouchPoint { ClientX = 200, ClientY = 100 }] });
        // The second finger lands beside the image, so only the viewer itself sees it.
        cut.Find(".gallery-lightbox").TouchStart(new TouchEventArgs { Touches = [new TouchPoint { ClientX = 200, ClientY = 100 }, new TouchPoint { ClientX = 400, ClientY = 100 }] });
        cut.Find(".gallery-image").TouchEnd(new TouchEventArgs { ChangedTouches = [new TouchPoint { ClientX = 100, ClientY = 100 }] });

        Assert.Equal("full-0.jpg", Src(cut));
    }

    [Fact]
    public void Navigating_ResetsZoom()
    {
        var cut = RenderLightbox();

        cut.Find(".gallery-nav-next").Click();

        Assert.Single(zoomModule.Invocations, i => i.Identifier == "reset");
    }

    [Fact]
    public void ProgressSegments_AreCappedAt100_AndJumpToTheirFirstImage()
    {
        var cut = RenderLightbox(count: 250);

        var segments = cut.FindAll(".gallery-progress-segment");
        Assert.Equal(100, segments.Count);
        Assert.Equal("Go to images 3 to 5 of 250", segments[1].GetAttribute("aria-label"));

        segments[1].Click();

        Assert.Equal("3 / 250", cut.Find(".gallery-index").TextContent);
        Assert.Contains("is-current", cut.FindAll(".gallery-progress-segment")[1].GetAttribute("class"));
    }

    [Fact]
    public void ItemName_IsUsedInAriaLabels()
    {
        var cut = RenderLightbox(configure: p => p.Add(x => x.ItemName, "photo"));

        Assert.Equal("Next photo", cut.Find(".gallery-nav-next").GetAttribute("aria-label"));
        Assert.Equal("Previous photo", cut.Find(".gallery-nav-prev").GetAttribute("aria-label"));
        Assert.Equal("Go to photo 1 of 3", cut.FindAll(".gallery-progress-segment")[0].GetAttribute("aria-label"));
    }

    [Fact]
    public void OriginalToggle_IsHidden_ForAnImageWithNoOriginal()
    {
        var cut = RenderLightbox(originalUrl: i => i == 1 ? "orig-1.jpg" : null);

        Assert.Empty(cut.FindAll(".gallery-original-toggle"));

        cut.Find(".gallery-nav-next").Click();

        Assert.Single(cut.FindAll(".gallery-original-toggle"));
    }

    [Fact]
    public void OriginalToggle_PersistsAcrossNavigation_AndRearmsTheLoadingSpinner()
    {
        var cut = RenderLightbox(originalUrl: i => $"orig-{i}.jpg");

        cut.Find(".gallery-original-toggle").Click();
        Assert.Equal("orig-0.jpg", Src(cut));
        Assert.Single(cut.FindAll(".gallery-image-loading"));

        cut.Find(".gallery-image").TriggerEvent("onload", new ProgressEventArgs());
        Assert.Empty(cut.FindAll(".gallery-image-loading"));

        cut.Find(".gallery-nav-next").Click();
        Assert.Equal("orig-1.jpg", Src(cut));
        Assert.Equal("true", cut.Find(".gallery-original-toggle").GetAttribute("aria-pressed"));
        Assert.Single(cut.FindAll(".gallery-image-loading"));

        cut.Find(".gallery-original-toggle").Click();
        Assert.Equal("full-1.jpg", Src(cut));
        Assert.Empty(cut.FindAll(".gallery-image-loading"));
    }

    [Fact]
    public void TitleAndActions_RenderForTheCurrentIndex()
    {
        var cut = RenderLightbox(configure: p => p
            .Add(x => x.Title, i => builder => builder.AddMarkupContent(0, $"<span class=\"title-marker\">title-{i}</span>"))
            .Add(x => x.Actions, i => builder => builder.AddMarkupContent(0, $"<span class=\"action-marker\">action-{i}</span>")));

        cut.Find(".gallery-nav-next").Click();

        Assert.Equal("title-1", cut.Find(".gallery-title-area .title-marker").TextContent);
        Assert.Equal("action-1", cut.Find(".gallery-topbar-actions .action-marker").TextContent);
    }

    [Fact]
    public void FocusesTheLightbox_WhenItOpens()
    {
        RenderLightbox();

        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public void TakesFocusBack_WhenADialogOverItCloses()
    {
        // e.g. Movie Detail's delete confirmation, opened from the viewer's Delete button
        // Once it closes, arrows and Esc should work again without a click.
        var cut = RenderLightbox(configure: p => p.Add(x => x.Covered, true));
        Assert.Single(JSInterop.Invocations, i => i.Identifier == "Blazor._internal.domWrapper.focus");

        cut.Render(p => p.Add(x => x.Covered, false));

        Assert.Equal(2, JSInterop.Invocations.Count(i => i.Identifier == "Blazor._internal.domWrapper.focus"));
    }

    [Fact]
    public void DoesNotRefocus_OnOrdinaryRerenders()
    {
        var cut = RenderLightbox();

        cut.Render(p => p.Add(x => x.Index, 1));

        Assert.Single(JSInterop.Invocations, i => i.Identifier == "Blazor._internal.domWrapper.focus");
    }
}
