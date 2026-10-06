using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>Covers MovieDetail's "Edit .nfo" modal end to end — BlazorMonaco drives it purely
/// through JS interop (no plain form control bUnit could render), and SaveRawNfoAsync does real
/// file I/O against LocalLibrary:RootPaths that only a live app instance resolves.</summary>
[Collection(E2ECollection.Name)]
public class NfoEditorFlowTests : IAsyncLifetime
{
    private readonly E2EFixture fixture;
    private readonly string scratchRoot = Path.Combine(Path.GetTempPath(), $"javbuddy-e2e-nfo-{Guid.NewGuid():N}");

    public NfoEditorFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    /// <summary>Copies the checked-in LocalLibraryFixtures tree into a throwaway per-test temp
    /// folder, so SaveNfo's real file write never touches the checked-in fixtures on disk.</summary>
    public Task InitializeAsync()
    {
        CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LocalLibraryFixtures"), scratchRoot);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(scratchRoot)) Directory.Delete(scratchRoot, recursive: true);
        return Task.CompletedTask;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
        }
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
        }
    }

    [Fact]
    public async Task EditViewDiffAndSave_PersistsToDiskAndPreservesEditsAcrossDiffToggle()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-NFO-1");
        await DbSeeding.SeedLocalLibraryRootPathAsync(fixture.App.Services, scratchRoot);
        var page = await fixture.NewPageAsync();

        await page.GotoInteractiveAsync($"/movies/{movie.Code}");
        await OpenNfoEditorAsync(page);

        var modal = page.Locator(".nfo-modal-panel");
        await Expect(modal).ToContainTextAsync("E2E NFO Fixture Movie");

        var saveButton = modal.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true });
        var viewDiffButton = modal.GetByRole(AriaRole.Button, new() { Name = "View diff" });
        await Expect(saveButton).ToBeDisabledAsync();
        await Expect(viewDiffButton).ToBeDisabledAsync();

        await modal.Locator(".view-lines").ClickAsync();
        await page.Keyboard.PressAsync("Control+End");
        await page.Keyboard.TypeAsync("<!-- e2e-edit-marker -->");

        await Expect(saveButton).ToBeEnabledAsync();
        await Expect(viewDiffButton).ToBeEnabledAsync();

        // Round-trip through the diff view — a fresh Monaco editor instance gets (re)created on
        // the way back, so this also proves the unsaved edit survives that swap.
        await viewDiffButton.ClickAsync();
        await Expect(modal.GetByRole(AriaRole.Button, new() { Name = "Back to editor" })).ToBeVisibleAsync();
        await Expect(modal).ToContainTextAsync("e2e-edit-marker");

        await modal.GetByRole(AriaRole.Button, new() { Name = "Back to editor" }).ClickAsync();
        await Expect(modal).ToContainTextAsync("e2e-edit-marker");

        await modal.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Expect(modal.Locator(".alert-success")).ToBeVisibleAsync();

        var nfoPath = Path.Combine(scratchRoot, "E2E-NFO-1", "E2E-NFO-1.nfo");
        var savedContent = await File.ReadAllTextAsync(nfoPath);
        Assert.Contains("e2e-edit-marker", savedContent);
        // The pre-edit file is kept in the database's .nfo history, not as a .bak
        Assert.False(File.Exists(nfoPath + ".bak"));

        // History shows that pre-edit version diffed against the file, and restores it.
        await modal.GetByRole(AriaRole.Button, new() { Name = "History" }).ClickAsync();
        await Expect(modal.Locator(".nfo-modal-title")).ToHaveTextAsync(".nfo History");
        await Expect(modal.Locator("select.nfo-history-select option")).ToHaveCountAsync(1);
        await Expect(modal.Locator("select.nfo-history-select option")).ToContainTextAsync("replaced by manual edit");
        await Expect(modal.Locator(".monaco-diff-editor")).ToBeVisibleAsync();
        await Expect(modal).ToContainTextAsync("e2e-edit-marker");

        await modal.GetByRole(AriaRole.Button, new() { Name = "Restore this version" }).ClickAsync();
        await Expect(modal.Locator(".alert-success")).ToBeVisibleAsync();
        await Expect(modal.Locator(".nfo-modal-title")).ToHaveTextAsync("Edit .nfo");
        Assert.DoesNotContain("e2e-edit-marker", await File.ReadAllTextAsync(nfoPath));
        Assert.False(File.Exists(nfoPath + ".bak"));
        await using var verify = await fixture.DbFactory.CreateDbContextAsync();
        var generations = verify.NfoGenerations.Where(g => g.MovieId == movie.Id).OrderByDescending(g => g.Id).ToList();
        Assert.Equal([NfoWriteTrigger.Restore, NfoWriteTrigger.ManualEdit], generations.Select(g => g.Trigger));
        Assert.Contains("e2e-edit-marker", generations[0].Content);
    }

    [Fact]
    public async Task InvalidXml_BlocksSaveWithError()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-NFO-2");
        await DbSeeding.SeedLocalLibraryRootPathAsync(fixture.App.Services, scratchRoot);
        var page = await fixture.NewPageAsync();

        await page.GotoInteractiveAsync($"/movies/{movie.Code}");
        await OpenNfoEditorAsync(page);

        var modal = page.Locator(".nfo-modal-panel");
        await Expect(modal).ToContainTextAsync("E2E NFO Fixture Movie 2");

        // Anything after the closing root tag is a "multiple root elements" parse error —
        // deterministic regardless of exact fixture content, unlike a malformed inner element.
        await modal.Locator(".view-lines").ClickAsync();
        await page.Keyboard.PressAsync("Control+End");
        await page.Keyboard.TypeAsync("<broken>");

        await modal.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        await Expect(modal.Locator(".alert-danger")).ToContainTextAsync("Can't save — invalid XML");

        var onDiskContent = await File.ReadAllTextAsync(Path.Combine(scratchRoot, "E2E-NFO-2", "E2E-NFO-2.nfo"));
        Assert.DoesNotContain("broken", onDiskContent);
    }

    /// <summary>Regression: Monaco puts both of its stylesheets into &lt;head&gt; from JS — the
    /// editor.main.css &lt;link&gt;, and a generated &lt;style class="monaco-colors"&gt; holding the
    /// token colours and theme variables — and Blazor's *enhanced navigation* rebuilds &lt;head&gt;
    /// from the newly-rendered page, silently dropping anything that only ever existed in the DOM.
    /// An editor opened after clicking through the app therefore painted unstyled (no link) and
    /// then, once the link was declared statically, in one flat colour with an invisible selection
    /// (no monaco-colors). Both tests above reach the page with GotoAsync, a full document load
    /// that re-runs Monaco's injection, which is exactly why they stayed green while the feature
    /// was broken — this one has to click through the grid.</summary>
    [Fact]
    public async Task OpenedAfterEnhancedNavigation_EditorIsStyledAndSyntaxHighlighted()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-NFO-3");
        await DbSeeding.SeedLocalLibraryRootPathAsync(fixture.App.Services, scratchRoot);
        var page = await fixture.NewPageAsync();

        await page.GotoInteractiveAsync("/");
        await page.GetByPlaceholder("Filter by code or title…").FillAsync(movie.Code!);
        await page.Locator($"a[href='/movies/{movie.Code}']").ClickAsync();

        await OpenNfoEditorAsync(page);
        var modal = page.Locator(".nfo-modal-panel");
        await Expect(modal).ToContainTextAsync("E2E NFO Fixture Movie 3");

        Assert.True(await page.Locator("head link[href*='editor.main.css']").CountAsync() >= 1);

        // editor.main.css is what gives .monaco-editor "position: relative"; without the stylesheet
        // the whole widget collapses to static-positioned, unstyled text spilling out of the modal.
        var position = await modal.Locator(".monaco-editor").First.EvaluateAsync<string>("el => getComputedStyle(el).position");
        Assert.Equal("relative", position);

        Assert.Equal(1, await page.Locator("head style.monaco-colors").CountAsync());

        // Monaco tokenizes into .mtk1/.mtk5/... spans whichever stylesheets are present, so the
        // markup alone proves nothing — only the *resolved* colours do. With monaco-colors missing
        // every class is unstyled and the whole document inherits one colour.
        await ExpectMultipleTokenColoursAsync(page, modal);
    }

    [Fact]
    public async Task ConflictReview_OpensMonacoDiffReview_AndSaveAndCloseResolvesConflict()
    {
        var movieFolder = Path.Combine(scratchRoot, "E2E-NFO-4");
        Directory.CreateDirectory(movieFolder);
        var nfoPath = Path.Combine(movieFolder, "E2E-NFO-4.nfo");
        var initialNfo = """
            <?xml version="1.0" encoding="UTF-8"?>
            <movie>
              <title>E2E NFO Conflict Movie</title>
              <id>E2E-NFO-4</id>
              <actor>
                <name>Yua Mikami</name>
              </actor>
            </movie>
            """;
        await File.WriteAllTextAsync(nfoPath, initialNfo);

        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-NFO-4");
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            var actor = new Javbuddy.Models.Actor { FirstName = "Yua", LastName = "Mikami" };
            actor.Aliases.Add(new Javbuddy.Models.ActorAlias { Name = "Yua Mikami" });
            db.Actors.Add(actor);
            await db.SaveChangesAsync();

            var dbMovie = await db.Movies.FindAsync(movie.Id);
            Assert.NotNull(dbMovie);
            dbMovie.NfoDriftKind = NfoDriftKind.ExternalEdit;
            dbMovie.NfoConflictDetails = ".nfo has 'Yua Mikami', canonical is 'Mikami Yua'";
            db.MovieActors.Add(new Javbuddy.Models.MovieActor { MovieId = movie.Id, ActorId = actor.Id });
            await db.SaveChangesAsync();
        }

        await DbSeeding.SeedLocalLibraryRootPathAsync(fixture.App.Services, scratchRoot);
        var page = await fixture.NewPageAsync();

        await page.GotoInteractiveAsync($"/movies/{movie.Code}");

        // The conflict badge is the affordance.
        var badge = page.Locator(".movie-detail-badge-warning");
        await Expect(badge).ToBeVisibleAsync();
        await Expect(badge).ToContainTextAsync("NFO: External edit");

        // Clicking the badge opens the Monaco "Resolve .nfo Conflict" diff review.
        await badge.ClickAsync();

        var modal = page.Locator(".nfo-modal-panel");
        await Expect(modal).ToBeVisibleAsync();
        await Expect(modal.Locator(".nfo-modal-title")).ToContainTextAsync("Resolve .nfo Conflict");
        await Expect(modal.Locator(".nfo-diff-pane-labels")).ToBeVisibleAsync();
        await Expect(modal).ToContainTextAsync("On-disk .nfo");
        await Expect(modal).ToContainTextAsync("Stored / Proposed .nfo");

        var discardBtn = modal.GetByRole(AriaRole.Button, new() { Name = "Discard changes" });
        var saveCloseBtn = modal.GetByRole(AriaRole.Button, new() { Name = "Save & close" });
        await Expect(discardBtn).ToBeVisibleAsync();
        await Expect(saveCloseBtn).ToBeVisibleAsync();

        // Discard closes the modal without persisting
        await discardBtn.ClickAsync();
        await Expect(modal).ToBeHiddenAsync();

        var onDiskAfterDiscard = await File.ReadAllTextAsync(nfoPath);
        Assert.Contains("<name>Yua Mikami</name>", onDiskAfterDiscard);
        Assert.DoesNotContain("<name>Mikami Yua</name>", onDiskAfterDiscard);
        await Expect(badge).ToBeVisibleAsync();

        // Open via the conflict badge and Save & close
        await badge.ClickAsync();
        await Expect(modal).ToBeVisibleAsync();
        await modal.GetByRole(AriaRole.Button, new() { Name = "Save & close" }).ClickAsync();

        // Modal should close and conflict should be resolved
        await Expect(modal).ToBeHiddenAsync();
        await Expect(badge).ToBeHiddenAsync();

        var onDiskAfterSave = await File.ReadAllTextAsync(nfoPath);
        Assert.Contains("<name>Mikami Yua</name>", onDiskAfterSave);
        Assert.False(File.Exists(nfoPath + ".bak"));
        await using var verify = await fixture.DbFactory.CreateDbContextAsync();
        var generation = Assert.Single(verify.NfoGenerations.Where(g => g.MovieId == movie.Id));
        Assert.Equal(NfoWriteTrigger.ConflictResolution, generation.Trigger);
        Assert.Equal(initialNfo, generation.Content);
    }

    /// <summary>Regression: the diff editor's Monaco model URIs were keyed off a per-NfoEditor-
    /// component-instance counter (nfoDiffEditorInstance) that resets to 0 for every movie, but
    /// the browser tab's Monaco "ModelService" — and the models this feature deliberately never
    /// disposes (see OnNfoDiffEditorInit's comment) — persists across Blazor's enhanced (same-
    /// circuit, same JS runtime) navigation between movies. The *first* conflict review opened
    /// for any movie therefore always built the same "nfo-diff-original-1"/"...-modified-1" URI,
    /// so opening it for a second movie after already opening it once for a first crashed the
    /// whole circuit with "ModelService: Cannot add model because it already exists!" — this must
    /// use GetByRole/Locator().ClickAsync() in-page navigation (not GotoAsync, which starts a
    /// fresh page load/circuit and JS runtime and could never reproduce this) to stay on the same
    /// circuit throughout, exactly like the real reported repro.</summary>
    [Fact]
    public async Task ConflictReview_OpenedForSecondMovieAfterFirst_DoesNotCrashOrShowStaleContent()
    {
        var folderA = Path.Combine(scratchRoot, "E2E-NFO-5");
        Directory.CreateDirectory(folderA);
        await File.WriteAllTextAsync(Path.Combine(folderA, "E2E-NFO-5.nfo"), """
            <?xml version="1.0" encoding="UTF-8"?>
            <movie>
              <title>E2E NFO Conflict Movie A</title>
              <id>E2E-NFO-5</id>
              <actor>
                <name>Stale Actor Name A</name>
              </actor>
            </movie>
            """);

        var folderB = Path.Combine(scratchRoot, "E2E-NFO-6");
        Directory.CreateDirectory(folderB);
        await File.WriteAllTextAsync(Path.Combine(folderB, "E2E-NFO-6.nfo"), """
            <?xml version="1.0" encoding="UTF-8"?>
            <movie>
              <title>E2E NFO Conflict Movie B</title>
              <id>E2E-NFO-6</id>
              <actor>
                <name>Stale Actor Name B</name>
              </actor>
            </movie>
            """);

        var movieA = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-NFO-5");
        var movieB = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-NFO-6");

        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            var actorA = new Javbuddy.Models.Actor { FirstName = "E2ENfoDiffCanonical", LastName = "ActorA" };
            actorA.Aliases.Add(new Javbuddy.Models.ActorAlias { Name = "Stale Actor Name A" });
            db.Actors.Add(actorA);
            var actorB = new Javbuddy.Models.Actor { FirstName = "E2ENfoDiffCanonical", LastName = "ActorB" };
            actorB.Aliases.Add(new Javbuddy.Models.ActorAlias { Name = "Stale Actor Name B" });
            db.Actors.Add(actorB);
            await db.SaveChangesAsync();

            var dbMovieA = await db.Movies.FindAsync(movieA.Id);
            Assert.NotNull(dbMovieA);
            dbMovieA.NfoDriftKind = NfoDriftKind.ExternalEdit;
            dbMovieA.NfoConflictDetails = ".nfo has 'Stale Actor Name A', canonical is 'ActorA E2ENfoDiffCanonical'";
            db.MovieActors.Add(new Javbuddy.Models.MovieActor { MovieId = movieA.Id, ActorId = actorA.Id });

            var dbMovieB = await db.Movies.FindAsync(movieB.Id);
            Assert.NotNull(dbMovieB);
            dbMovieB.NfoDriftKind = NfoDriftKind.ExternalEdit;
            dbMovieB.NfoConflictDetails = ".nfo has 'Stale Actor Name B', canonical is 'ActorB E2ENfoDiffCanonical'";
            db.MovieActors.Add(new Javbuddy.Models.MovieActor { MovieId = movieB.Id, ActorId = actorB.Id });

            await db.SaveChangesAsync();
        }

        await DbSeeding.SeedLocalLibraryRootPathAsync(fixture.App.Services, scratchRoot);
        var page = await fixture.NewPageAsync();

        // First navigation of the test — this is the only allowed GotoAsync; it establishes the
        // one circuit/JS runtime the rest of the test stays on.
        await page.GotoInteractiveAsync($"/movies/{movieA.Code}");

        var conflictBadge = page.Locator(".movie-detail-badge-warning");
        var modal = page.Locator(".nfo-modal-panel");

        await conflictBadge.ClickAsync();
        await Expect(modal).ToBeVisibleAsync();
        await Expect(modal).ToContainTextAsync("Stale Actor Name A");
        await modal.GetByRole(AriaRole.Button, new() { Name = "Discard changes" }).ClickAsync();
        await Expect(modal).ToBeHiddenAsync();

        // In-page (enhanced) navigation to movie B — no GotoAsync, so the same circuit/JS runtime
        // (and its Monaco ModelService) carries over from movie A above.
        await page.Locator("nav.nav a.nav-link", new() { HasText = "Movies" }).ClickAsync();
        await page.GetByPlaceholder("Filter by code or title…").FillAsync(movieB.Code!);
        await page.Locator($"a[href='/movies/{movieB.Code}']").ClickAsync();

        await conflictBadge.ClickAsync();

        // Before the fix: this crashed the circuit (JSException: "ModelService: Cannot add model
        // because it already exists!"), which would make every assertion below fail/time out.
        await Expect(modal).ToBeVisibleAsync();
        await Expect(modal).ToContainTextAsync("Stale Actor Name B");
        await Expect(modal).Not.ToContainTextAsync("Stale Actor Name A");
    }

    /// <summary>Regression: BlazorMonaco's component Dispose() only releases its
    /// .NET object reference — it never disposes the Monaco widget — so every open, close and
    /// edit/diff toggle left a live editor (and its text models) behind in the tab for the rest of
    /// the session. The models must return to the page's baseline once the modal is closed, the
    /// page is left by enhanced navigation with the modal still open, or a conflict review is
    /// discarded, without Monaco's disposed-model or duplicate-URI errors and without breaking the
    /// theme or the unsaved edit carried through the diff view.</summary>
    [Fact]
    public async Task RepeatedOpenCloseTogglesAndNavigation_ReleaseMonacoEditorsAndModels()
    {
        var folderA = Path.Combine(scratchRoot, "E2E-NFO-7");
        Directory.CreateDirectory(folderA);
        await File.WriteAllTextAsync(Path.Combine(folderA, "E2E-NFO-7.nfo"), """
            <?xml version="1.0" encoding="UTF-8"?>
            <movie>
              <title>E2E NFO Lifecycle Movie A</title>
              <id>E2E-NFO-7</id>
              <actor>
                <name>Lifecycle Stale Name</name>
              </actor>
            </movie>
            """);
        var folderB = Path.Combine(scratchRoot, "E2E-NFO-8");
        Directory.CreateDirectory(folderB);
        await File.WriteAllTextAsync(Path.Combine(folderB, "E2E-NFO-8.nfo"), """
            <?xml version="1.0" encoding="UTF-8"?>
            <movie>
              <title>E2E NFO Lifecycle Movie B</title>
              <id>E2E-NFO-8</id>
            </movie>
            """);

        var movieA = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-NFO-7");
        var movieB = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-NFO-8");
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            var actor = new Javbuddy.Models.Actor { FirstName = "E2ENfoLifecycle", LastName = "Canonical" };
            actor.Aliases.Add(new Javbuddy.Models.ActorAlias { Name = "Lifecycle Stale Name" });
            db.Actors.Add(actor);
            await db.SaveChangesAsync();

            var dbMovieA = await db.Movies.FindAsync(movieA.Id);
            Assert.NotNull(dbMovieA);
            dbMovieA.NfoDriftKind = NfoDriftKind.ExternalEdit;
            dbMovieA.NfoConflictDetails = ".nfo has 'Lifecycle Stale Name', canonical is 'Canonical E2ENfoLifecycle'";
            db.MovieActors.Add(new Javbuddy.Models.MovieActor { MovieId = movieA.Id, ActorId = actor.Id });
            await db.SaveChangesAsync();
        }

        await DbSeeding.SeedLocalLibraryRootPathAsync(fixture.App.Services, scratchRoot);
        var page = await fixture.NewPageAsync();

        var monacoErrors = new List<string>();
        page.Console += (_, msg) =>
        {
            if (msg.Type == "error") monacoErrors.Add(msg.Text);
        };
        page.PageError += (_, error) => monacoErrors.Add(error);

        // Movies grid → movie A by in-page navigation, so the whole test stays on one circuit and
        // one Monaco ModelService, exactly where leaked models would accumulate.
        await page.GotoInteractiveAsync("/");
        await page.GetByPlaceholder("Filter by code or title…").FillAsync(movieA.Code!);
        await page.Locator($"a[href='/movies/{movieA.Code}']").ClickAsync();
        // Monaco is only loaded once the editor opens, and its warm-up editor leaves no model.
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Tools", Exact = true })).ToBeEnabledAsync();
        Assert.True(await page.EvaluateAsync<bool>("() => typeof window.monaco === 'undefined'"));
        Assert.Equal(0, await page.Locator("link[href*='editor.main.css'], script[src*='BlazorMonaco']").CountAsync());
        const int baseline = 0;

        var modal = page.Locator(".nfo-modal-panel");

        for (var i = 0; i < 3; i++)
        {
            await OpenNfoEditorAsync(page);
            await Expect(modal).ToContainTextAsync("E2E NFO Lifecycle Movie A");

            var marker = $"lifecycle-marker-{i}";
            await modal.Locator(".view-lines").ClickAsync();
            await page.Keyboard.PressAsync("Control+End");
            await page.Keyboard.TypeAsync($"<!-- {marker} -->");

            await modal.GetByRole(AriaRole.Button, new() { Name = "View diff" }).ClickAsync();
            await Expect(modal.Locator(".nfo-diff-pane-labels")).ToBeVisibleAsync();
            await Expect(modal).ToContainTextAsync(marker);

            await modal.GetByRole(AriaRole.Button, new() { Name = "Back to editor" }).ClickAsync();
            await Expect(modal.Locator(".nfo-diff-pane-labels")).ToBeHiddenAsync();
            await Expect(modal).ToContainTextAsync(marker);

            await modal.GetByRole(AriaRole.Button, new() { Name = "View diff" }).ClickAsync();
            await Expect(modal).ToContainTextAsync(marker);

            // Discard from the diff view goes back to a fresh editor holding the on-disk file.
            await modal.GetByRole(AriaRole.Button, new() { Name = "Discard changes" }).ClickAsync();
            await Expect(modal.Locator(".nfo-diff-pane-labels")).ToBeHiddenAsync();
            await Expect(modal).Not.ToContainTextAsync(marker);

            await modal.Locator(".nfo-modal-close-btn").ClickAsync();
            await Expect(modal).ToBeHiddenAsync();
            await ExpectMonacoModelCountAsync(page, baseline);
        }

        await page.Locator(".movie-detail-badge-warning").ClickAsync();
        await Expect(modal).ToContainTextAsync("Lifecycle Stale Name");
        await modal.GetByRole(AriaRole.Button, new() { Name = "Discard changes" }).ClickAsync();
        await Expect(modal).ToBeHiddenAsync();
        await ExpectMonacoModelCountAsync(page, baseline);

        // Leave movie A with the diff view still open: going back is an enhanced navigation, so
        // NfoEditor is disposed while its editor is mounted and the circuit carries on.
        await page.Locator(".movie-detail-badge-warning").ClickAsync();
        await Expect(modal).ToContainTextAsync("Lifecycle Stale Name");
        await page.GoBackAsync();
        await Expect(modal).ToBeHiddenAsync();
        await ExpectMonacoModelCountAsync(page, baseline);

        await page.GetByPlaceholder("Filter by code or title…").FillAsync(movieB.Code!);
        await page.Locator($"a[href='/movies/{movieB.Code}']").ClickAsync();
        await OpenNfoEditorAsync(page);
        await Expect(modal).ToContainTextAsync("E2E NFO Lifecycle Movie B");

        Assert.Equal(1, await page.Locator("head style.monaco-colors").CountAsync());
        // The editor.main.css <link> is injected when Monaco loads, so it's dropped by the same
        // enhanced navigation; without it .monaco-editor isn't "position: relative".
        Assert.Equal("relative", await modal.Locator(".monaco-editor").First.EvaluateAsync<string>("el => getComputedStyle(el).position"));
        await ExpectMultipleTokenColoursAsync(page, modal);

        await page.GoBackAsync();
        await Expect(modal).ToBeHiddenAsync();
        await ExpectMonacoModelCountAsync(page, baseline);

        Assert.DoesNotContain(monacoErrors, e => e.Contains("disposed", StringComparison.OrdinalIgnoreCase)
            || e.Contains("already exists", StringComparison.OrdinalIgnoreCase));
    }

    // Monaco paints the text before its tokenizer colours it, so this waits for the colours rather
    // than sampling once: a slow runner catches the plain first paint.
    private static async Task ExpectMultipleTokenColoursAsync(IPage page, ILocator modal)
    {
        const string distinctColours =
            "el => new Set([...el.querySelectorAll('span[class^=mtk]')].map(s => getComputedStyle(s).color)).size";
        var viewLines = await modal.Locator(".view-lines").ElementHandleAsync();
        try
        {
            await page.WaitForFunctionAsync($"el => ({distinctColours})(el) > 1", viewLines, new() { Timeout = 10000 });
        }
        catch (TimeoutException)
        {
            // Fall through to the assertion below, which reports the actual count.
        }
        var count = await viewLines.EvaluateAsync<int>(distinctColours);
        Assert.True(count > 1, $"Expected XML tokens to render in multiple colours, got {count}.");
    }

    /// <summary>Edit .nfo lives in Movie Detail's Tools menu.</summary>
    private static async Task OpenNfoEditorAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Tools", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Edit .nfo" }).ClickAsync();
    }

    private static Task<int> MonacoModelCountAsync(IPage page) =>
        page.EvaluateAsync<int>("() => window.monaco.editor.getModels().length");

    private static async Task ExpectMonacoModelCountAsync(IPage page, int expected)
    {
        try
        {
            await page.WaitForFunctionAsync(
                "expected => window.monaco.editor.getModels().length === expected",
                expected,
                new() { Timeout = 5000 });
        }
        catch (TimeoutException)
        {
            // Fall through to the assertion below, which reports the actual count.
        }
        Assert.Equal(expected, await MonacoModelCountAsync(page));
        Assert.Equal(0, await page.EvaluateAsync<int>("() => window.blazorMonaco.editors.length"));
    }

    /// <summary>End to end: the Movies toolbar's bulk push, gated on a drift filter, runs as
    /// a real background job (BackgroundJobRunner) that writes the real .nfo on disk.</summary>
    [Fact]
    public async Task BulkPush_FromMoviesToolbar_WritesJavbuddyMetadataToTheNfo()
    {
        var movieFolder = Path.Combine(scratchRoot, "E2E-NFO-PUSH");
        Directory.CreateDirectory(movieFolder);
        var nfoPath = Path.Combine(movieFolder, "E2E-NFO-PUSH.nfo");
        await File.WriteAllTextAsync(nfoPath, "<movie>\n  <title>Old Title</title>\n</movie>");

        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-NFO-PUSH", metaTitle: "New Title");
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            var dbMovie = await db.Movies.FindAsync(movie.Id);
            Assert.NotNull(dbMovie);
            dbMovie.NfoDriftKind = NfoDriftKind.JavbuddyChanged;
            dbMovie.NfoConflictDetails = "[Javbuddy changed] Title: .nfo has 'Old Title', canonical is 'New Title'";
            await db.SaveChangesAsync();
        }

        await DbSeeding.SeedLocalLibraryRootPathAsync(fixture.App.Services, scratchRoot);
        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync("/");
        await page.GetByPlaceholder("Filter by code or title…").FillAsync("E2E-NFO-PUSH");
        await Expect(page.Locator(".poster-card")).ToHaveCountAsync(1);

        var pushButton = page.GetByRole(AriaRole.Button, new() { Name = "Write Javbuddy metadata to .nfo" });
        await Expect(pushButton).ToHaveCountAsync(0);

        await page.Locator("button.dropdown-main-btn", new() { HasTextString = "Filter" }).ClickAsync();
        await page.Locator("button.sort-dropdown-item", new() { HasTextString = "NFO drift" }).ClickAsync();
        await page.Locator("button.sort-dropdown-subitem", new() { HasTextString = "Javbuddy changed" }).ClickAsync();
        await Expect(page.Locator(".poster-card")).ToHaveCountAsync(1);
        // Close the filter dropdown: its full-page backdrop would otherwise intercept the click.
        await page.Locator("button.dropdown-main-btn", new() { HasTextString = "Filter" }).ClickAsync();
        await Expect(page.Locator(".dropdown-backdrop")).ToHaveCountAsync(0);

        await pushButton.ClickAsync();
        var modal = page.Locator(".nfo-push-modal-panel");
        await Expect(modal).ToContainTextAsync("Javbuddy changed (1)");
        await modal.GetByRole(AriaRole.Button, new() { Name = "Write 1 .nfo file(s)" }).ClickAsync();
        await Expect(modal).ToBeHiddenAsync();

        // The job writes in the background; once it's done the drift clears and the grid
        // (filtered to Javbuddy changed) empties via MovieChangeNotifier.
        await Expect(page.Locator(".poster-card")).ToHaveCountAsync(0);
        Assert.Equal("<movie>\n  <title>New Title</title>\n</movie>", await File.ReadAllTextAsync(nfoPath));
        Assert.False(File.Exists(nfoPath + ".bak"));
        await using (var verify = await fixture.DbFactory.CreateDbContextAsync())
        {
            var generation = Assert.Single(verify.NfoGenerations.Where(g => g.MovieId == movie.Id));
            Assert.Equal(NfoWriteTrigger.DriftPush, generation.Trigger);
            Assert.Equal("<movie>\n  <title>Old Title</title>\n</movie>", generation.Content);
            Assert.Equal(NfoDriftKind.None, (await verify.Movies.FindAsync(movie.Id))!.NfoDriftKind);
        }
    }
}
