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

        var heading = cut.Find(".delete-confirm-dialog h2");
        Assert.Equal("Delete \"Interview\"?", heading.TextContent);
        Assert.Equal(heading.Id, cut.Find("[role=dialog]").GetAttribute("aria-labelledby"));
        Assert.Equal("Its screenshot is deleted too.", cut.Find(".delete-confirm-dialog p").TextContent);
        JSInterop.VerifyFocusAsyncInvoke();

        Assert.Empty(RenderDialog().FindAll(".delete-confirm-dialog p"));
    }

    [Fact]
    public void RendersChildContentBetweenTheMessageAndTheButtons()
    {
        var cut = Render<DeleteConfirmDialog>(p => p
            .Add(x => x.ItemName, "Interview")
            .AddChildContent("<label class=\"extra-option\">Delete files from disk</label>"));

        var dialog = cut.Find(".delete-confirm-dialog");
        Assert.Equal("Delete files from disk", cut.Find(".extra-option").TextContent);
        Assert.Equal("delete-confirm-actions", dialog.LastElementChild!.ClassName);
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
}
