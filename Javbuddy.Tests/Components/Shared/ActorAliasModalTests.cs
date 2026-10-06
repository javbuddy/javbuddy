using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

public class ActorAliasModalTests : BunitContext
{
    [Fact]
    public void Modal_WhenShowFalse_RendersNothing()
    {
        var actorService = Substitute.For<IActorService>();
        Services.AddSingleton(actorService);

        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };
        var cut = Render<ActorAliasModal>(p => p
            .Add(x => x.Show, false)
            .Add(x => x.Actor, actor));

        Assert.Empty(cut.FindAll(".alias-modal-backdrop"));
    }

    [Fact]
    public void Modal_WhenShowTrue_RendersActorAliases_WithRemoveButtons()
    {
        var actorService = Substitute.For<IActorService>();
        Services.AddSingleton(actorService);

        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };
        actor.Aliases.Add(new ActorAlias { Id = 10, Name = "Alias One" });
        actor.Aliases.Add(new ActorAlias { Id = 20, Name = "Alias Two" });

        var cut = Render<ActorAliasModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Actor, actor));

        Assert.Contains("Manage Aliases — Mikami Yua", cut.Find(".alias-modal-title").TextContent);

        var rows = cut.FindAll(".alias-row");
        Assert.Equal(2, rows.Count);

        foreach (var row in rows)
        {
            Assert.NotNull(row.QuerySelector(".alias-remove-btn"));
        }
    }

    [Fact]
    public async Task Modal_AddingNewAlias_CallsActorServiceAddAliasAsync_AndTriggersOnAliasesChanged()
    {
        var actorService = Substitute.For<IActorService>();
        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };

        actorService.AddAliasAsync(1, "New Alternate Name", Arg.Any<CancellationToken>())
            .Returns(ActorOperationResult.Ok(actor));
        Services.AddSingleton(actorService);

        var changedInvoked = false;
        var cut = Render<ActorAliasModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Actor, actor)
            .Add(x => x.OnAliasesChanged, () => changedInvoked = true));

        var input = cut.Find("#new-alias-input");
        input.Change("New Alternate Name");

        var form = cut.Find("form");
        await cut.InvokeAsync(() => form.Submit());

        await actorService.Received(1).AddAliasAsync(1, "New Alternate Name", Arg.Any<CancellationToken>());
        Assert.True(changedInvoked);
        Assert.Contains("Added alias \"New Alternate Name\"", cut.Find(".alert-success").TextContent);
    }

    [Fact]
    public async Task Modal_AddingNewAlias_WhenFails_DisplaysErrorMessage()
    {
        var actorService = Substitute.For<IActorService>();
        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };

        actorService.AddAliasAsync(1, "Conflicting Name", Arg.Any<CancellationToken>())
            .Returns(ActorOperationResult.Fail("Alias \"Conflicting Name\" is already assigned to another actor."));
        Services.AddSingleton(actorService);

        var changedInvoked = false;
        var cut = Render<ActorAliasModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Actor, actor)
            .Add(x => x.OnAliasesChanged, () => changedInvoked = true));

        var input = cut.Find("#new-alias-input");
        input.Change("Conflicting Name");

        var form = cut.Find("form");
        await cut.InvokeAsync(() => form.Submit());

        Assert.False(changedInvoked);
        Assert.Contains("already assigned to another actor", cut.Find(".alert-danger").TextContent);
    }

    [Fact]
    public async Task Modal_RemovingAlias_CallsActorServiceRemoveAliasAsync_AndTriggersOnAliasesChanged()
    {
        var actorService = Substitute.For<IActorService>();
        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };
        actor.Aliases.Add(new ActorAlias { Id = 42, Name = "Test Alias" });

        actorService.RemoveAliasAsync(1, 42, Arg.Any<CancellationToken>())
            .Returns(ActorOperationResult.Ok(actor));
        Services.AddSingleton(actorService);

        var changedInvoked = false;
        var cut = Render<ActorAliasModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Actor, actor)
            .Add(x => x.OnAliasesChanged, () => changedInvoked = true));

        var removeBtn = cut.Find(".alias-remove-btn");
        await cut.InvokeAsync(() => removeBtn.Click());

        await actorService.Received(1).RemoveAliasAsync(1, 42, Arg.Any<CancellationToken>());
        Assert.True(changedInvoked);
        Assert.Contains("Alias removed.", cut.Find(".alert-success").TextContent);
    }

    [Fact]
    public async Task Modal_CloseActions_TriggerOnClose()
    {
        var actorService = Substitute.For<IActorService>();
        Services.AddSingleton(actorService);

        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };
        var closeCount = 0;
        var cut = Render<ActorAliasModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Actor, actor)
            .Add(x => x.OnClose, () => closeCount++));

        // Header close button
        var closeBtn = cut.Find(".alias-modal-close-btn");
        await cut.InvokeAsync(() => closeBtn.Click());
        Assert.Equal(1, closeCount);

        // Escape key
        var backdrop = cut.Find(".alias-modal-backdrop");
        await cut.InvokeAsync(() => backdrop.KeyDown(new KeyboardEventArgs { Key = "Escape" }));
        Assert.Equal(2, closeCount);
    }
}
