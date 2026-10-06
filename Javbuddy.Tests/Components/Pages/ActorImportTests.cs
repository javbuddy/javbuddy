using Bunit;
using Javbuddy.Components.Pages;
using Javbuddy.Services.Actors;
using Javbuddy.Services.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages;

public class ActorImportTests : BunitContext
{
    [Fact]
    public void ActorImport_RendersInitialState()
    {
        var discoveryService = Substitute.For<IActorDiscoveryService>();
        Services.AddSingleton(discoveryService);

        var cut = Render<ActorImport>();

        Assert.Contains("Import actors from your movies", cut.Markup);
        var sourceSelect = cut.Find("select#import-source-select");
        Assert.NotNull(sourceSelect);
        Assert.Contains("Local Library (.nfo / filesystem)", sourceSelect.TextContent);

        var button = cut.Find("button.import-btn");
        Assert.Contains("Scan Local Library for Actors", button.TextContent);
        Assert.False(button.HasAttribute("disabled"));
        Assert.DoesNotContain("aliases", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ActorImport_ClickingScan_RendersCandidateTable()
    {
        var candidate1 = DiscoveredActorCandidate.FromName("Araki Noa", "SIVR-505");
        candidate1.JapaneseKanji = "新木希空";
        candidate1.Thumb = "https://example.com/araki.jpg";
        candidate1.Source = "Local (.nfo)";

        var candidate2 = DiscoveredActorCandidate.FromName("Mikami Yua", "ABP-123");
        candidate2.Source = "Local (cast)";

        var discoveryService = Substitute.For<IActorDiscoveryService>();
        discoveryService.ScanAsync(ActorDiscoverySource.LocalLibrary, Arg.Any<IProgress<TaskProgress>>(), Arg.Any<CancellationToken>())
            .Returns(new ActorScanResult(
                TotalFound: 5,
                AlreadyTracked: 3,
                Candidates: [candidate1, candidate2]));
        Services.AddSingleton(discoveryService);

        var cut = Render<ActorImport>();
        var scanButton = cut.Find("button.import-btn");

        await cut.InvokeAsync(() => scanButton.Click());

        await discoveryService.Received(1).ScanAsync(ActorDiscoverySource.LocalLibrary, Arg.Any<IProgress<TaskProgress>>(), Arg.Any<CancellationToken>());

        Assert.Contains("5 actors", cut.Markup);
        Assert.Contains("3 already tracked", cut.Markup);

        var table = cut.Find("table.candidate-table");
        Assert.NotNull(table);
        Assert.Contains("Araki Noa", table.TextContent);
        Assert.Contains("新木希空", table.TextContent);
        Assert.Contains("Mikami Yua", table.TextContent);
        Assert.Contains("Local (.nfo)", table.TextContent);
        Assert.Contains("Local (cast)", table.TextContent);
        Assert.DoesNotContain("<th>Aliases</th>", cut.Markup);

        // Selection controls
        var importSelectedBtn = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Import Selected"));
        Assert.NotNull(importSelectedBtn);
        Assert.True(importSelectedBtn.HasAttribute("disabled")); // 0 selected initially

        var importAllBtn = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Import All (2)"));
        Assert.NotNull(importAllBtn);
    }

    [Fact]
    public async Task ActorImport_SelectCandidateAndImportSelected_CallsImportCandidatesAsync()
    {
        var candidate1 = DiscoveredActorCandidate.FromName("Araki Noa", "SIVR-505");
        var candidate2 = DiscoveredActorCandidate.FromName("Mikami Yua", "ABP-123");

        var discoveryService = Substitute.For<IActorDiscoveryService>();
        discoveryService.ScanAsync(ActorDiscoverySource.LocalLibrary, Arg.Any<IProgress<TaskProgress>>(), Arg.Any<CancellationToken>())
            .Returns(new ActorScanResult(
                TotalFound: 2,
                AlreadyTracked: 0,
                Candidates: [candidate1, candidate2]));

        discoveryService.ImportCandidatesAsync(Arg.Any<IReadOnlyList<DiscoveredActorCandidate>>(), Arg.Any<IProgress<TaskProgress>>(), Arg.Any<CancellationToken>())
            .Returns(new ActorImportResult(1, 1, ["Araki Noa"], []));

        Services.AddSingleton(discoveryService);

        var cut = Render<ActorImport>();
        await cut.InvokeAsync(() => cut.Find("button.import-btn").Click());

        // Check candidate 1 checkbox
        var checkboxes = cut.FindAll("input.candidate-checkbox");
        Assert.Equal(2, checkboxes.Count);

        await cut.InvokeAsync(() => checkboxes[0].Change(true));

        var importSelectedBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Import Selected (1)"));
        Assert.False(importSelectedBtn.HasAttribute("disabled"));

        await cut.InvokeAsync(() => importSelectedBtn.Click());

        await discoveryService.Received(1).ImportCandidatesAsync(
            Arg.Is<IReadOnlyList<DiscoveredActorCandidate>>(list => list.Count == 1 && list[0].DisplayName == "Araki Noa"),
            Arg.Any<IProgress<TaskProgress>>(),
            Arg.Any<CancellationToken>());

        Assert.Contains("Successfully imported 1 new actor", cut.Markup);
        Assert.Contains("Araki Noa", cut.Find("ul.import-result-list").TextContent);
    }

    [Fact]
    public async Task ActorImport_ClickingImportAll_ImportsAllCandidates()
    {
        var candidate1 = DiscoveredActorCandidate.FromName("Araki Noa", "SIVR-505");
        var candidate2 = DiscoveredActorCandidate.FromName("Mikami Yua", "ABP-123");

        var discoveryService = Substitute.For<IActorDiscoveryService>();
        discoveryService.ScanAsync(ActorDiscoverySource.LocalLibrary, Arg.Any<IProgress<TaskProgress>>(), Arg.Any<CancellationToken>())
            .Returns(new ActorScanResult(
                TotalFound: 2,
                AlreadyTracked: 0,
                Candidates: [candidate1, candidate2]));

        discoveryService.ImportCandidatesAsync(Arg.Any<IReadOnlyList<DiscoveredActorCandidate>>(), Arg.Any<IProgress<TaskProgress>>(), Arg.Any<CancellationToken>())
            .Returns(new ActorImportResult(2, 2, ["Araki Noa", "Mikami Yua"], []));

        Services.AddSingleton(discoveryService);

        var cut = Render<ActorImport>();
        await cut.InvokeAsync(() => cut.Find("button.import-btn").Click());

        var importAllBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Import All (2)"));
        await cut.InvokeAsync(() => importAllBtn.Click());

        await discoveryService.Received(1).ImportCandidatesAsync(
            Arg.Is<IReadOnlyList<DiscoveredActorCandidate>>(list => list.Count == 2),
            Arg.Any<IProgress<TaskProgress>>(),
            Arg.Any<CancellationToken>());

        Assert.Contains("Successfully imported 2 new actors", cut.Markup);
        var resultList = cut.Find("ul.import-result-list");
        Assert.Contains("Araki Noa", resultList.TextContent);
        Assert.Contains("Mikami Yua", resultList.TextContent);
    }

    [Fact]
    public async Task ActorImport_ImportFailure_RendersRejectedActorAndReason()
    {
        var candidate = DiscoveredActorCandidate.FromName("Duplicate Actor", "ABC-123");
        var discoveryService = Substitute.For<IActorDiscoveryService>();
        discoveryService.ScanAsync(ActorDiscoverySource.LocalLibrary, Arg.Any<IProgress<TaskProgress>>(), Arg.Any<CancellationToken>())
            .Returns(new ActorScanResult(1, 0, [candidate]));
        discoveryService.ImportCandidatesAsync(Arg.Any<IReadOnlyList<DiscoveredActorCandidate>>(), Arg.Any<IProgress<TaskProgress>>(), Arg.Any<CancellationToken>())
            .Returns(new ActorImportResult(
                0,
                0,
                [],
                [new ActorImportFailure("Duplicate Actor", "An actor with the same first and last name already exists.")]));
        Services.AddSingleton(discoveryService);

        var cut = Render<ActorImport>();
        await cut.InvokeAsync(() => cut.Find("button.import-btn").Click());
        await cut.InvokeAsync(() => cut.FindAll("button").First(button => button.TextContent.Contains("Import All")).Click());

        var warning = cut.Find(".alert-warning");
        Assert.Contains("Could not import 1 actor", warning.TextContent);
        Assert.Contains("Duplicate Actor", warning.TextContent);
        Assert.Contains("same first and last name", warning.TextContent);
    }

    [Fact]
    public async Task ActorImport_CandidateWithThumbnailDataUrl_RendersDataUrlImage()
    {
        var candidate = DiscoveredActorCandidate.FromName("Araki Noa", "SIVR-505");
        candidate.ThumbnailDataUrl = "data:image/webp;base64,UklGRk...";

        var discoveryService = Substitute.For<IActorDiscoveryService>();
        discoveryService.ScanAsync(ActorDiscoverySource.LocalLibrary, Arg.Any<IProgress<TaskProgress>>(), Arg.Any<CancellationToken>())
            .Returns(new ActorScanResult(
                TotalFound: 1,
                AlreadyTracked: 0,
                Candidates: [candidate]));

        Services.AddSingleton(discoveryService);

        var cut = Render<ActorImport>();
        await cut.InvokeAsync(() => cut.Find("button.import-btn").Click());

        var img = cut.Find("div.candidate-avatar-photo img");
        Assert.NotNull(img);
        var src = img.GetAttribute("src");
        Assert.Equal("data:image/webp;base64,UklGRk...", src);
        Assert.Equal("Araki Noa", img.GetAttribute("alt"));
    }

    [Fact]
    public async Task ActorImport_CandidateWithoutImage_RendersInitials()
    {
        var candidate = DiscoveredActorCandidate.FromName("Mikami Yua", "ABP-123");

        var discoveryService = Substitute.For<IActorDiscoveryService>();
        discoveryService.ScanAsync(ActorDiscoverySource.LocalLibrary, Arg.Any<IProgress<TaskProgress>>(), Arg.Any<CancellationToken>())
            .Returns(new ActorScanResult(
                TotalFound: 1,
                AlreadyTracked: 0,
                Candidates: [candidate]));

        Services.AddSingleton(discoveryService);

        var cut = Render<ActorImport>();
        await cut.InvokeAsync(() => cut.Find("button.import-btn").Click());

        var avatar = cut.Find("div.candidate-avatar");
        Assert.False(avatar.ClassList.Contains("candidate-avatar-photo"));
        Assert.Equal("MY", avatar.TextContent.Trim());
        Assert.Empty(cut.FindAll("div.candidate-avatar img"));
    }

    [Fact]
    public async Task ActorImport_ClickingCancel_CancelsScan()
    {
        var tcs = new TaskCompletionSource<ActorScanResult>();
        var discoveryService = Substitute.For<IActorDiscoveryService>();
        discoveryService.ScanAsync(ActorDiscoverySource.LocalLibrary, Arg.Any<IProgress<TaskProgress>>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var ct = callInfo.Arg<CancellationToken>();
                await using (ct.Register(() => tcs.TrySetCanceled(ct)))
                {
                    return await tcs.Task;
                }
            });
        Services.AddSingleton(discoveryService);

        var cut = Render<ActorImport>();
        var scanButton = cut.Find("button.import-btn");
        await cut.InvokeAsync(() => scanButton.Click());

        var cancelBtn = cut.Find("button.btn-outline-danger");
        Assert.NotNull(cancelBtn);
        Assert.Equal("Cancel", cancelBtn.TextContent.Trim());

        await cut.InvokeAsync(() => cancelBtn.Click());

        Assert.Contains("Scan was cancelled.", cut.Markup);
        Assert.False(cut.Find("button.import-btn").HasAttribute("disabled"));
    }

    [Fact]
    public async Task ActorImport_ClickingCancel_CancelsImport()
    {
        var candidate = DiscoveredActorCandidate.FromName("Araki Noa", "SIVR-505");
        var discoveryService = Substitute.For<IActorDiscoveryService>();
        discoveryService.ScanAsync(ActorDiscoverySource.LocalLibrary, Arg.Any<IProgress<TaskProgress>>(), Arg.Any<CancellationToken>())
            .Returns(new ActorScanResult(1, 0, [candidate]));

        var tcs = new TaskCompletionSource<ActorImportResult>();
        discoveryService.ImportCandidatesAsync(Arg.Any<IReadOnlyList<DiscoveredActorCandidate>>(), Arg.Any<IProgress<TaskProgress>>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var ct = callInfo.Arg<CancellationToken>();
                await using (ct.Register(() => tcs.TrySetCanceled(ct)))
                {
                    return await tcs.Task;
                }
            });

        Services.AddSingleton(discoveryService);

        var cut = Render<ActorImport>();
        await cut.InvokeAsync(() => cut.Find("button.import-btn").Click());

        var importAllBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Import All"));
        await cut.InvokeAsync(() => importAllBtn.Click());

        var cancelBtn = cut.Find("button.btn-outline-danger");
        Assert.NotNull(cancelBtn);
        await cut.InvokeAsync(() => cancelBtn.Click());

        Assert.Contains("Import was cancelled.", cut.Markup);
    }

    [Fact]
    public async Task ActorImport_WhileImporting_DisplaysProgressStageAndDisablesButtonsWithImportingText()
    {
        var candidate1 = DiscoveredActorCandidate.FromName("Araki Noa", "SIVR-505");
        var candidate2 = DiscoveredActorCandidate.FromName("Mikami Yua", "ABP-123");
        var discoveryService = Substitute.For<IActorDiscoveryService>();
        discoveryService.ScanAsync(ActorDiscoverySource.LocalLibrary, Arg.Any<IProgress<TaskProgress>>(), Arg.Any<CancellationToken>())
            .Returns(new ActorScanResult(2, 0, [candidate1, candidate2]));

        var tcs = new TaskCompletionSource<ActorImportResult>();
        discoveryService.ImportCandidatesAsync(Arg.Any<IReadOnlyList<DiscoveredActorCandidate>>(), Arg.Any<IProgress<TaskProgress>>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var ct = callInfo.Arg<CancellationToken>();
                await using (ct.Register(() => tcs.TrySetCanceled(ct)))
                {
                    return await tcs.Task;
                }
            });

        Services.AddSingleton(discoveryService);

        var cut = Render<ActorImport>();
        await cut.InvokeAsync(() => cut.Find("button.import-btn").Click());

        var importAllBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Import All (2)"));
        await cut.InvokeAsync(() => importAllBtn.Click());

        // While import is running:
        var candidateProgress = cut.Find(".candidate-progress-label");
        Assert.Contains("Importing actors (0/2)…", candidateProgress.TextContent);

        var cancelBtn = cut.Find("button.btn-outline-danger");
        Assert.NotNull(cancelBtn);
        Assert.Equal("Cancel", cancelBtn.TextContent.Trim());

        var inlineProgress = cut.Find(".candidate-inline-progress");
        Assert.NotNull(inlineProgress);

        var buttons = cut.FindAll("button");
        var importButtons = buttons.Where(b => b.TextContent.Trim() == "Importing…").ToList();
        Assert.Equal(2, importButtons.Count); // Both "Import Selected" and "Import All" say "Importing…"
        Assert.All(importButtons, b => Assert.True(b.HasAttribute("disabled")));

        // Clean up
        tcs.TrySetResult(new ActorImportResult(2, 2, ["Araki Noa", "Mikami Yua"], []));
    }
}
