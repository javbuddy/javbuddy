using System.Xml.Linq;
using Javbuddy.Models;
using Javbuddy.Services.Nfo;

namespace Javbuddy.Tests.Services.Nfo;

/// <summary>The pure direction/baseline rules behind NFO drift — no DB or filesystem, just a Movie
/// (Javbuddy's side), parsed .nfo values (disk's side) and the stored baseline.</summary>
public class NfoDriftDetectorTests
{
    private static Movie MovieWith(string? title = "Title A", string? plot = "Plot A", params string[] actorFirstNames)
    {
        var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Got, MetaTitle = title, MetaDescription = plot };
        foreach (var firstName in actorFirstNames)
        {
            movie.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = firstName, LastName = "Test" } });
        }
        return movie;
    }

    private static NfoFieldValues Disk(string xml) => NfoDriftDetector.ReadNfo(XElement.Parse(xml));

    private static NfoFieldValues DiskWith(string? title = "Title A", string? plot = "Plot A") =>
        Disk($"<movie><title>{title}</title><plot>{plot}</plot></movie>");

    /// <summary>A baseline recorded while Javbuddy and disk agreed on title "Title A" / plot "Plot A".</summary>
    private static NfoBaselineState AgreedBaseline() =>
        NfoDriftDetector.Evaluate(MovieWith(), DiskWith(), baseline: null, strict: false).Baseline;

    [Fact]
    public void CanonicalGenres_IncludeTagsTheMovieOnlyHasThroughItsClips()
    {
        var movie = new Movie { Code = "C-001" };
        movie.MovieTags.Add(new MovieTag { Movie = movie, Tag = new Tag { Name = "Drama" } });
        movie.MovieTags.Add(new MovieTag { Movie = movie, Tag = new Tag { Name = "Squirt" }, IsExplicit = false, FromClips = true });

        // The .nfo lists rolled-up clip tags too.
        Assert.Equal(["Drama", "Squirt"], NfoDriftDetector.CanonicalGenres(movie));
    }

    [Fact]
    public void Evaluate_NoDrift_ReturnsNoneAndBaselinesEveryField()
    {
        var outcome = NfoDriftDetector.Evaluate(MovieWith(), DiskWith(), baseline: null, strict: false);

        Assert.Equal(NfoDriftKind.None, outcome.Kind);
        Assert.Null(outcome.Details);
        Assert.Equal(Enum.GetValues<NfoField>().Length, outcome.Baseline.Fields.Count);
        Assert.Equal("Title A", outcome.Baseline.LastDisk?.Title);
    }

    [Fact]
    public void Evaluate_OnlyJavbuddyChangedSinceBaseline_IsJavbuddyChanged()
    {
        var outcome = NfoDriftDetector.Evaluate(MovieWith(title: "Title B"), DiskWith(), AgreedBaseline(), strict: false);

        Assert.Equal(NfoDriftKind.JavbuddyChanged, outcome.Kind);
        Assert.Equal("[Javbuddy changed] Title: .nfo has 'Title A', canonical is 'Title B'", outcome.Details);
    }

    [Fact]
    public void Evaluate_OnlyDiskChangedSinceBaseline_IsExternalEdit()
    {
        var outcome = NfoDriftDetector.Evaluate(MovieWith(), DiskWith(title: "Title C"), AgreedBaseline(), strict: false);

        Assert.Equal(NfoDriftKind.ExternalEdit, outcome.Kind);
        Assert.Equal("[External edit] Title: .nfo has 'Title C', canonical is 'Title A'", outcome.Details);
    }

    [Fact]
    public void Evaluate_BothChangedDifferently_IsBothChanged()
    {
        var outcome = NfoDriftDetector.Evaluate(MovieWith(title: "Title B"), DiskWith(title: "Title C"), AgreedBaseline(), strict: false);

        Assert.Equal(NfoDriftKind.BothChanged, outcome.Kind);
        Assert.StartsWith("[Both changed] Title:", outcome.Details);
    }

    [Fact]
    public void Evaluate_BothChangedToTheSameValue_IsNoDriftAndAdvancesTheBaseline()
    {
        var outcome = NfoDriftDetector.Evaluate(MovieWith(title: "Title B"), DiskWith(title: "Title B"), AgreedBaseline(), strict: false);
        Assert.Equal(NfoDriftKind.None, outcome.Kind);

        // Advanced: an external edit back to the old value now reads as external, not "both".
        var next = NfoDriftDetector.Evaluate(MovieWith(title: "Title B"), DiskWith(title: "Title A"), outcome.Baseline, strict: false);
        Assert.Equal(NfoDriftKind.ExternalEdit, next.Kind);
    }

    [Fact]
    public void Evaluate_FirstObservationAlreadyDiffering_IsExternalEdit()
    {
        var outcome = NfoDriftDetector.Evaluate(MovieWith(title: "Title B"), DiskWith(), baseline: null, strict: false);

        Assert.Equal(NfoDriftKind.ExternalEdit, outcome.Kind);
        Assert.False(outcome.Baseline.Fields.ContainsKey(NfoField.Title));
        Assert.True(outcome.Baseline.Fields.ContainsKey(NfoField.Plot));
    }

    [Fact]
    public void Evaluate_NoHistoryAndValueMissingOnDisk_IsJavbuddyChanged()
    {
        // Only strict matching flags a value the .nfo lacks entirely; with no history to go on,
        // that's something Javbuddy has that the file doesn't, not an outside edit.
        var outcome = NfoDriftDetector.Evaluate(MovieWith(), Disk("<movie><plot>Plot A</plot></movie>"), baseline: null, strict: true);

        Assert.Equal(NfoDriftKind.JavbuddyChanged, outcome.Kind);
        Assert.Equal("[Javbuddy changed] Title: .nfo has '(none)', canonical is 'Title A'", outcome.Details);
    }

    [Fact]
    public void Evaluate_DriftingFieldKeepsItsBaselineWhileOtherFieldsAdvance()
    {
        // Title drifts (Javbuddy changed); plot changes on both sides to the same value.
        var first = NfoDriftDetector.Evaluate(MovieWith(title: "Title B", plot: "Plot B"), DiskWith(plot: "Plot B"), AgreedBaseline(), strict: false);
        Assert.Equal(NfoDriftKind.JavbuddyChanged, first.Kind);

        // Now disk edits title too: still judged against the original agreed title, so "both".
        var second = NfoDriftDetector.Evaluate(MovieWith(title: "Title B", plot: "Plot B"), DiskWith(title: "Title C", plot: "Plot B"), first.Baseline, strict: false);
        Assert.Equal(NfoDriftKind.BothChanged, second.Kind);
        Assert.DoesNotContain("Plot", second.Details);
    }

    [Fact]
    public void Evaluate_KindIsTheWorstAcrossFields_AndDetailsListEachField()
    {
        var outcome = NfoDriftDetector.Evaluate(MovieWith(title: "Title B"), DiskWith(plot: "Plot C"), AgreedBaseline(), strict: false);

        Assert.Equal(NfoDriftKind.ExternalEdit, outcome.Kind);
        Assert.Equal(
            "[Javbuddy changed] Title: .nfo has 'Title A', canonical is 'Title B'; [External edit] Plot: .nfo has 'Plot C', canonical is 'Plot A'",
            outcome.Details);
    }

    [Fact]
    public void Evaluate_JavbuddysOwnWriteToAStillDriftingField_IsNotMistakenForAnExternalEdit()
    {
        const string agreedXml = "<movie><title>Title A</title><plot>Plot A</plot><actor><name>Test Aoi</name></actor></movie>";
        var baseline = NfoDriftDetector.Evaluate(MovieWith(null, null, "Aoi"), Disk(agreedXml), baseline: null, strict: false).Baseline;

        // Javbuddy links a second actor; an actor sync writes a partial fix to the actors on disk
        // (e.g. renaming an alias) that still leaves the second actor missing.
        var movie = MovieWith(null, null, "Aoi", "Rin");
        var preWrite = Disk(agreedXml);
        var postWrite = Disk("<movie><title>Title A</title><plot>Plot A</plot><actor><name>Test Aoi</name><thumb>x</thumb><alias>Aoi T</alias></actor></movie>");

        var outcome = NfoDriftDetector.Evaluate(movie, postWrite, baseline, strict: false, preWriteDisk: preWrite);

        Assert.Equal(NfoDriftKind.JavbuddyChanged, outcome.Kind);
        Assert.Equal("[Javbuddy changed] Actor 'Test Rin' missing in .nfo", outcome.Details);
    }

    [Fact]
    public void Evaluate_ActorMatchingStillUsesDiskAliases()
    {
        var movie = MovieWith(null, null, "Aoi");
        var disk = Disk("<movie><actor><name>Someone Else</name><alias>Test Aoi</alias></actor><actor><name>Other</name></actor></movie>");

        var outcome = NfoDriftDetector.Evaluate(movie, disk, baseline: null, strict: false);

        Assert.Equal("[External edit] .nfo has 'Someone Else', canonical is 'Test Aoi'", outcome.Details);
    }

    [Fact]
    public void BaselineState_RoundTripsThroughJson()
    {
        var baseline = NfoDriftDetector.Evaluate(
            MovieWith(null, null, "Aoi"),
            Disk("<movie><title>T</title><releasedate>2020-05-01</releasedate><rating>7.5</rating><genre>Drama</genre><actor><name>Test Aoi</name><altname>葵</altname></actor></movie>"),
            baseline: null, strict: false).Baseline;

        var roundTripped = NfoBaselineState.FromJson(baseline.ToJson());

        Assert.NotNull(roundTripped);
        Assert.Equal(baseline.ToJson(), roundTripped.ToJson());
    }
}
