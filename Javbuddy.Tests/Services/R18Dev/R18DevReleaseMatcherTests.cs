using Javbuddy.Models;
using Javbuddy.Services.R18Dev;

namespace Javbuddy.Tests.Services.R18Dev;

public class R18DevReleaseMatcherTests
{
    [Fact]
    public void Classify_DeletedCanonicalVariant_IsDistinctFromMissingAndInLibrary()
    {
        var deleted = new DeletedMovie { Code = "MIDE-001", NormalizedCode = "MIDE-001", CanonicalKey = Javbuddy.Services.Movies.CodeNormalization.GetCanonicalKey("MIDE-001") };
        var rows = R18DevReleaseMatcher.Classify([Entry("MIDE-00001"), Entry("MIDE-002")], [], [deleted]);

        Assert.True(rows[0].PreviouslyDeleted);
        Assert.False(rows[0].InLibrary);
        Assert.Equal("Previously Deleted", rows[0].StatusLabel);
        Assert.Equal("/movies/MIDE-00001", rows[0].LinkUrl);
        Assert.Equal("missing", rows[1].StatusClass);
    }

    [Fact]
    public void Classify_ActiveMovieWinsOverDeletionHistory_AndReAddUpdatesDeletedSiblings()
    {
        var deleted = new DeletedMovie { Code = "MIDE-001", NormalizedCode = "MIDE-001", CanonicalKey = Javbuddy.Services.Movies.CodeNormalization.GetCanonicalKey("MIDE-001") };
        var entries = new[] { Entry("MIDE-001"), Entry("MIDE-00001") };
        var active = R18DevReleaseMatcher.Classify(entries, [Local("MIDE-001", MovieStatus.Got)], [deleted]);
        Assert.All(active, r => Assert.Equal("got", r.StatusClass));

        var rows = R18DevReleaseMatcher.Classify(entries, [], [deleted]);
        var updated = R18DevReleaseMatcher.ApplyAddedMovie(rows, Local("MIDE-001", MovieStatus.Missing));
        Assert.All(updated, r => Assert.True(r.InLibrary));
        Assert.All(updated, r => Assert.False(r.PreviouslyDeleted));
    }

    [Fact]
    public void CollapseDuplicates_DeletedSiblingPassesItsStatusToCleanestCode()
    {
        var rows = new[]
        {
            new R18DevReleaseRow(Entry("MIDE-001DOD"), "deleted", "Previously Deleted", "/movies/MIDE-001DOD", "MIDE1"),
            new R18DevReleaseRow(Entry("MIDE-001"), "missing", "Missing", "/movies/MIDE-001", "MIDE1"),
        };
        var row = Assert.Single(R18DevReleaseMatcher.CollapseDuplicates(rows));
        Assert.Equal("MIDE-001", row.Movie.DvdId);
        Assert.True(row.PreviouslyDeleted);
        Assert.False(row.InLibrary);
    }

    private static R18DevFilmographyEntry Entry(string dvdId, string? poster = null, DateOnly? released = null, string? title = "Title") =>
        new(dvdId, title, null, released, poster);

    private static Movie Local(string code, MovieStatus status) => new() { Code = code, Status = status };

    [Fact]
    public void Classify_UntrackedRelease_IsMissingWithReleaseDateAndPreviewLink()
    {
        var rows = R18DevReleaseMatcher.Classify(new[] { Entry("MIDE-001", released: new DateOnly(2020, 1, 2)) }, Array.Empty<Movie>());

        var row = Assert.Single(rows);
        Assert.Equal("missing", row.StatusClass);
        Assert.Equal("2020-01-02", row.StatusLabel);
        Assert.Equal("/movies/MIDE-001", row.LinkUrl);
        Assert.False(row.InLibrary);
    }

    [Fact]
    public void Classify_UntrackedReleaseWithoutDate_LabelsItMissing()
    {
        var row = Assert.Single(R18DevReleaseMatcher.Classify(new[] { Entry("MIDE-001") }, Array.Empty<Movie>()));

        Assert.Equal("Missing", row.StatusLabel);
    }

    [Fact]
    public void Classify_TrackedGotMovie_IsGotAndLinksToTheLocalCode()
    {
        var rows = R18DevReleaseMatcher.Classify(new[] { Entry("MIDE-001") }, new[] { Local("mide-001", MovieStatus.Got) });

        var row = Assert.Single(rows);
        Assert.Equal("got", row.StatusClass);
        Assert.Equal("Got", row.StatusLabel);
        Assert.Equal("/movies/mide-001", row.LinkUrl);
        Assert.True(row.InLibrary);
    }

    [Fact]
    public void Classify_TrackedButNotGotMovie_IsWanted()
    {
        var row = Assert.Single(R18DevReleaseMatcher.Classify(new[] { Entry("MIDE-001") }, new[] { Local("MIDE-001", MovieStatus.Missing) }));

        Assert.Equal("wanted", row.StatusClass);
        Assert.True(row.InLibrary);
    }

    [Fact]
    public void Classify_ZeroPaddingDifference_StillMatchesTheLocalMovie()
    {
        var row = Assert.Single(R18DevReleaseMatcher.Classify(new[] { Entry("MIDE-00001") }, new[] { Local("MIDE-001", MovieStatus.Got) }));

        Assert.Equal("got", row.StatusClass);
    }

    [Fact]
    public void Classify_LocalMovieWithoutCode_IsIgnored()
    {
        var rows = R18DevReleaseMatcher.Classify(new[] { Entry("MIDE-001") }, new[] { new Movie { Code = null } });

        Assert.Equal("missing", Assert.Single(rows).StatusClass);
    }

    [Fact]
    public void ApplyAddedMovie_FlipsMatchingRowsAndLeavesOthersAlone()
    {
        var rows = R18DevReleaseMatcher.Classify(new[] { Entry("MIDE-001"), Entry("MIDE-002") }, Array.Empty<Movie>());

        var updated = R18DevReleaseMatcher.ApplyAddedMovie(rows, Local("MIDE-001", MovieStatus.Missing));

        Assert.Equal("wanted", updated.Single(r => r.Movie.DvdId == "MIDE-001").StatusClass);
        Assert.Equal("/movies/MIDE-001", updated.Single(r => r.Movie.DvdId == "MIDE-001").LinkUrl);
        Assert.Equal("missing", updated.Single(r => r.Movie.DvdId == "MIDE-002").StatusClass);
    }

    [Fact]
    public void CollapseDuplicates_SameCanonicalKey_KeepsTheCleanestCode()
    {
        var rows = R18DevReleaseMatcher.Classify(
            new[] { Entry("MIDE-001DOD"), Entry("MIDE-001") },
            Array.Empty<Movie>());

        var collapsed = R18DevReleaseMatcher.CollapseDuplicates(rows);

        Assert.Equal("MIDE-001", Assert.Single(collapsed).Movie.DvdId);
    }

    [Fact]
    public void CollapseDuplicates_WhenASiblingIsTracked_TheSurvivorInheritsItsStatus()
    {
        // The local library tracks the distributor variant, but the cleaner code wins the collapse
        // — it must still show as in-library rather than "missing".
        var rows = R18DevReleaseMatcher.Classify(
            new[] { Entry("MIDE-001DOD"), Entry("MIDE-001") },
            new[] { Local("MIDE-001DOD", MovieStatus.Got) });

        var survivor = Assert.Single(R18DevReleaseMatcher.CollapseDuplicates(rows));

        Assert.Equal("MIDE-001", survivor.Movie.DvdId);
        Assert.Equal("got", survivor.StatusClass);
        Assert.Equal("/movies/MIDE-001DOD", survivor.LinkUrl);
    }
}
