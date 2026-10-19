using Javbuddy.Components.Shared;
using Javbuddy.Models;
using Javbuddy.Services.Movies;

namespace Javbuddy.Tests.Components.Shared;

public class MovieFilterOptionsTests
{
    [Fact]
    public void MigrateLegacyNfoFeatures_MovesOldNfoOptionsIntoTheGroup_AndLeavesOtherFeaturesAlone()
    {
        var features = new HashSet<MovieFeatureFilterOption>
        {
            MovieFeatureFilterOption.HasSubtitles,
            MovieFeatureFilterOption.LegacyNfoDriftExternalEdit,
            MovieFeatureFilterOption.LegacyNfoDriftBothChanged,
        };
        var kinds = new HashSet<NfoDriftKind>();

        MovieFilterOptions.MigrateLegacyNfoFeatures(features, kinds);

        Assert.Equal([MovieFeatureFilterOption.HasSubtitles], features);
        Assert.Equal([NfoDriftKind.ExternalEdit, NfoDriftKind.BothChanged], kinds.Order());
    }

    [Fact]
    public void MigrateLegacyNfoFeatures_OldAnyOption_SelectsEveryDirection()
    {
        var features = new HashSet<MovieFeatureFilterOption> { MovieFeatureFilterOption.LegacyNfoConflict };
        var kinds = new HashSet<NfoDriftKind>();

        MovieFilterOptions.MigrateLegacyNfoFeatures(features, kinds);

        Assert.Empty(features);
        Assert.Equal(MovieFilterOptions.NfoDriftKinds.Order(), kinds.Order());
    }

    [Fact]
    public void Features_NoLongerOffersAnyNfoOption()
    {
        Assert.DoesNotContain(MovieFilterOptions.Features, f => f.ToString().Contains("Nfo"));
        // The legacy values keep their saved-cookie integers.
        Assert.Equal(3, (int)MovieFeatureFilterOption.LegacyNfoConflict);
        Assert.Equal(8, (int)MovieFeatureFilterOption.LegacyNfoDriftJavbuddyChanged);
    }

    [Fact]
    public void Features_NoLongerOffersHasScenes_ButKeepsItsSavedValue()
    {
        Assert.Equal(
            [MovieFeatureFilterOption.Favorites, MovieFeatureFilterOption.HasFavoriteScene],
            MovieFilterOptions.Features.Take(2));
        Assert.DoesNotContain(MovieFeatureFilterOption.HasScenes, MovieFilterOptions.Features);
        // Appended, so a saved Movies view-state cookie keeps its meaning.
        Assert.Equal(11, (int)MovieFeatureFilterOption.HasFavoriteScene);
        Assert.Equal(12, (int)MovieFeatureFilterOption.HasScenes);
    }

    [Fact]
    public void MigrateLegacyHasScenes_BecomesAtLeastOneScene_KeepingAStricterMinimum()
    {
        var features = new HashSet<MovieFeatureFilterOption> { MovieFeatureFilterOption.HasScenes, MovieFeatureFilterOption.Favorites };

        var migrated = MovieFilterOptions.MigrateLegacyHasScenes(features, new MovieCountSelection { Scenes = new IntRange(null, 5) });
        Assert.Equal(new IntRange(1, 5), migrated.Scenes);
        Assert.Equal([MovieFeatureFilterOption.Favorites], features);

        var stricter = MovieFilterOptions.MigrateLegacyHasScenes(
            new HashSet<MovieFeatureFilterOption> { MovieFeatureFilterOption.HasScenes }, new MovieCountSelection { Scenes = new IntRange(3, null) });
        Assert.Equal(new IntRange(3, null), stricter.Scenes);

        var untouched = new MovieCountSelection { Apexes = new IntRange(2, null) };
        Assert.Same(untouched, MovieFilterOptions.MigrateLegacyHasScenes(new HashSet<MovieFeatureFilterOption>(), untouched));
    }

    [Fact]
    public void NormalizeCounts_DropsEndsAtOrPastTheBounds_AndRangesNobodyOffers()
    {
        var options = MovieCountOptions.FromMaximums(maxScenes: 6, maxHighlights: 0, maxApexes: 3);
        var saved = new MovieCountSelection
        {
            Scenes = new IntRange(0, 6),
            Highlights = new IntRange(1, 2),
            Apexes = new IntRange(2, 9),
        };

        var normalized = MovieFilterOptions.NormalizeCounts(saved, options);

        Assert.Equal(IntRange.None, normalized.Scenes);
        Assert.Equal(IntRange.None, normalized.Highlights);
        Assert.Equal(new IntRange(2, null), normalized.Apexes);
    }

    [Fact]
    public void FromMaximums_OffersZeroToTheMaximum_AndNothingWhenNoMovieHasAny()
    {
        var options = MovieCountOptions.FromMaximums(4, 0, 1);

        Assert.Equal(new RangeBounds(0, 4), options.Scenes);
        Assert.Null(options.Highlights);
        Assert.Equal(new RangeBounds(0, 1), options.Apexes);
        Assert.True(options.HasAny);
        Assert.False(MovieCountOptions.FromMaximums(0, 0, 0).HasAny);
    }

    private static ActorAttributeOptions Offered(string[]? cups = null, RangeBounds? height = null, RangeBounds? age = null) =>
        new() { CupSizes = cups ?? [], Height = height, Age = age };

    [Fact]
    public void Normalize_DropsUnofferedCups_IgnoringCase()
    {
        var result = MovieFilterOptions.NormalizeActorAttributes(
            new ActorAttributeSelection { CupSizes = ["k", "E"] }, Offered(cups: ["D", "E"]));

        Assert.Equal(["E"], result.CupSizes);
    }

    [Theory]
    [InlineData(148, 170, 150, 170, null, null)]
    [InlineData(175, null, 150, 170, 170, null)]
    [InlineData(null, 140, 150, 170, null, 150)]
    [InlineData(155, 165, 150, 170, 155, 165)]
    [InlineData(160, 165, null, null, null, null)]
    public void Normalize_ReconcilesEachRangeWithItsBounds(int? min, int? max, int? lo, int? hi, int? expectedMin, int? expectedMax)
    {
        var selection = new ActorAttributeSelection { Age = new IntRange(min, max), Bust = new IntRange(min, max) };
        var options = Offered(age: lo is { } l && hi is { } h ? new RangeBounds(l, h) : null);

        var result = MovieFilterOptions.NormalizeActorAttributes(selection, options);

        Assert.Equal(new IntRange(expectedMin, expectedMax), result.Age);
        Assert.Equal(IntRange.None, result.Bust); // no bust bound offered
    }

    [Fact]
    public void Merge_KeepsWhatTheViewDoesNotOffer_AndTakesEditsForWhatItDoes()
    {
        var current = new ActorAttributeSelection { CupSizes = ["K", "E"], Height = new IntRange(150, 160), Age = new IntRange(20, 30) };
        var edited = new ActorAttributeSelection { CupSizes = ["D"], Height = new IntRange(155, null) };
        var options = Offered(cups: ["D", "E"], height: new RangeBounds(145, 175)); // no age bound here

        var merged = MovieFilterOptions.MergeActorAttributes(current, edited, options);

        Assert.Equal(["K", "D"], merged.CupSizes);            // K not offered: kept; E offered: replaced by the edit
        Assert.Equal(new IntRange(155, null), merged.Height); // offered: edited value
        Assert.Equal(new IntRange(20, 30), merged.Age);       // not offered: kept
    }

    [Fact]
    public void Selection_CountsChipsAndSetRanges_AndRoundTripsThroughJson()
    {
        var selection = new ActorAttributeSelection { CupSizes = ["E", "F"], Bust = new IntRange(80, null), Age = new IntRange(null, 30) };

        Assert.Equal(4, selection.Count);
        Assert.False(selection.IsEmpty);
        Assert.True(ActorAttributeSelection.Empty.IsEmpty);

        var round = System.Text.Json.JsonSerializer.Deserialize<ActorAttributeSelection>(System.Text.Json.JsonSerializer.Serialize(selection))!;
        Assert.Equal(selection.CupSizes, round.CupSizes);
        Assert.Equal(selection.Bust, round.Bust);
        Assert.Equal(selection.Age, round.Age);
        Assert.Equal(IntRange.None, System.Text.Json.JsonSerializer.Deserialize<ActorAttributeSelection>("{}")!.Height);
        Assert.Equal(new RangeBounds(1, 2), System.Text.Json.JsonSerializer.Deserialize<ActorAttributeOptions>(
            System.Text.Json.JsonSerializer.Serialize(new ActorAttributeOptions { Hips = new RangeBounds(1, 2) }))!.Hips);
    }

    [Fact]
    public void Merge_KeepsTheRememberedRange_WhenTheViewOnlyShowedItClamped()
    {
        // Remembered Height 150-160; this view's bounds start at 152, so it shows and emits "no min, 160".
        var current = new ActorAttributeSelection { Height = new IntRange(150, 160) };
        var options = Offered(cups: ["D", "E"], height: new RangeBounds(152, 170));
        var shown = MovieFilterOptions.NormalizeActorAttributes(current, options);
        Assert.Equal(new IntRange(null, 160), shown.Height);

        var merged = MovieFilterOptions.MergeActorAttributes(current, shown with { CupSizes = ["E"] }, options); // only the cup changed

        Assert.Equal(new IntRange(150, 160), merged.Height);   // untouched: the remembered 150 survives
        Assert.Equal(["E"], merged.CupSizes);

        var moved = MovieFilterOptions.MergeActorAttributes(current, shown with { Height = new IntRange(155, 160) }, options);
        Assert.Equal(new IntRange(155, 160), moved.Height);    // actually edited: taken
    }

    [Fact]
    public void Options_HasAny_IsTrueWhenAnyChipOrRangeIsOffered()
    {
        Assert.False(ActorAttributeOptions.None.HasAny);
        Assert.True(new ActorAttributeOptions { CupSizes = ["E"] }.HasAny);
        Assert.True(new ActorAttributeOptions { Waist = new RangeBounds(55, 65) }.HasAny);
    }
}
