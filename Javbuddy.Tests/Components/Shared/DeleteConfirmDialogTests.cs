using Bunit;
using Javbuddy.Components.Shared;
using Microsoft.AspNetCore.Components.Web;

namespace Javbuddy.Tests.Components.Shared;

public class DeleteConfirmDialogTests : BunitContext
{
    private int confirmed;
    private int cancelled;

    public DeleteConfirmDialogTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    private IRenderedComponent<DeleteConfirmDialog> RenderDialog(string? message = null, bool busy = false) =>
        Render<DeleteConfirmDialog>(p => p
            .Add(x => x.ItemName, "Interview")
            .Add(x => x.Message, message)
            .Add(x => x.Busy, busy)
            .Add(x => x.OnConfirm, () => confirmed++)
            .Add(x => x.OnCancel, () => cancelled++));

    [Fact]
    public void NamesTheItem_ShowsTheOptionalMessage_AndFocusesCancel()
    {
        var cut = RenderDialog("Its screenshot is deleted too.");

        var heading = cut.Find(".modal-shell-title");
        Assert.Equal("Delete \"Interview\"?", heading.TextContent);
        Assert.Equal(heading.Id, cut.Find("[role=dialog]").GetAttribute("aria-labelledby"));
        Assert.Equal("Its screenshot is deleted too.", cut.Find(".delete-confirm-message").TextContent);
        JSInterop.VerifyFocusAsyncInvoke();

        Assert.Empty(RenderDialog().FindAll(".delete-confirm-message"));
    }

    [Fact]
    public void RendersChildContentBetweenTheMessageAndTheButtons()
    {
        var cut = Render<DeleteConfirmDialog>(p => p
            .Add(x => x.ItemName, "Interview")
            .AddChildContent("<label class=\"extra-option\">Delete files from disk</label>"));

        Assert.Equal("Delete files from disk", cut.Find(".modal-shell-body .extra-option").TextContent);
        Assert.NotNull(cut.Find(".modal-shell-footer .delete-confirm-confirm-btn"));
    }

    [Fact]
    public void Delete_Confirms()
    {
        RenderDialog().Find(".delete-confirm-confirm-btn").Click();

        Assert.Equal((1, 0), (confirmed, cancelled));
    }

    [Fact]
    public void CancelEscapeAndBackdrop_Cancel_OtherKeysAndDialogClicksDont()
    {
        var cut = RenderDialog();

        cut.Find(".delete-confirm-cancel-btn").Click();
        cut.Find(".delete-confirm-backdrop").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        cut.Find(".delete-confirm-backdrop").Click();
        cut.Find(".delete-confirm-backdrop").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal((0, 3), (confirmed, cancelled));
    }

    [Fact]
    public void WhileBusy_ButtonsAreDisabledAndNothingCancels()
    {
        var cut = RenderDialog(busy: true);

        Assert.True(cut.Find(".delete-confirm-cancel-btn").HasAttribute("disabled"));
        Assert.True(cut.Find(".delete-confirm-confirm-btn").HasAttribute("disabled"));
        cut.Find(".delete-confirm-backdrop").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        cut.Find(".delete-confirm-backdrop").Click();

        Assert.Equal(0, cancelled);
    }

    [Fact]
    public void SitsAboveTheDrawerAndKeepsClicksAndKeysToItself()
    {
        var backdrop = RenderDialog().Find(".modal-shell-backdrop");

        Assert.Contains("z-index: 1060", backdrop.GetAttribute("style"));
        Assert.Contains("delete-confirm-backdrop", backdrop.ClassName);
        var attrs = string.Join(",", backdrop.Attributes.Select(a => a.Name));
        Assert.Contains("onkeydown:stopPropagation", attrs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("onclick:stopPropagation", attrs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Themed_FollowsTheSiteTheme_OnlyWhenAsked()
    {
        Assert.Empty(RenderDialog().FindAll(".modal-shell-themed"));

        var cut = Render<DeleteConfirmDialog>(p => p.Add(x => x.ItemName, "x").Add(x => x.Themed, true));

        Assert.Single(cut.FindAll(".delete-confirm-backdrop.modal-shell-themed"));
    }

    [Fact]
    public void TitleAndConfirmText_ReplaceTheDefaults()
    {
        var cut = Render<DeleteConfirmDialog>(p => p
            .Add(x => x.Title, "Delete Remu?")
            .Add(x => x.ConfirmText, "Delete Actor"));

        Assert.Equal("Delete Remu?", cut.Find(".modal-shell-title").TextContent);
        Assert.Equal("Delete Actor", cut.Find(".delete-confirm-confirm-btn").TextContent);
    }
}
