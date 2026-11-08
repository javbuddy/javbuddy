using Javbuddy.Models;
using Javbuddy.Services.Api;
using Javbuddy.Services.Api.Contracts;

namespace Javbuddy.Tests.Services.Api;

public class MovieMetadataPatchTests
{
    private static Movie Current() => new()
    {
        Code = "ABC-123",
        Title = "Override",
        MetaTitle = "Meta",
        MetaOriginalTitle = "Original",
        MetaDescription = "Desc",
        MetaReleaseDate = new DateTime(2024, 1, 2),
        MetaStudio = "Studio",
        MetaLabel = "Label",
        MetaSeries = "Series",
        MetaDirector = "Director",
        ContentType = MovieContentType.Uncensored,
    };

    [Fact]
    public void Apply_SetOneField_KeepsEveryOtherCurrentValue()
    {
        var result = MovieMetadataPatch.Apply(Current(), new MetadataPatchRequest(new MetadataFields(Description: "  New  ")));

        Assert.True(result.IsOk);
        var update = result.Value!;
        Assert.Equal("New", update.MetaDescription);
        Assert.Equal("Override", update.Title);
        Assert.Equal("Meta", update.MetaTitle);
        Assert.Equal("Original", update.MetaOriginalTitle);
        Assert.Equal(new DateTime(2024, 1, 2), update.MetaReleaseDate);
        Assert.Equal("Studio", update.MetaStudio);
        Assert.Equal("Label", update.MetaLabel);
        Assert.Equal("Series", update.MetaSeries);
        Assert.Equal("Director", update.MetaDirector);
        Assert.Equal(MovieContentType.Uncensored, update.ContentType);
    }

    [Fact]
    public void Apply_MapsEachPublicNameToItsMovieField()
    {
        var set = new MetadataFields("t", "to", "ot", "d", new DateOnly(2020, 5, 6), "s", "l", "se", "di", "DECENSORED");

        var update = MovieMetadataPatch.Apply(Current(), new MetadataPatchRequest(set)).Value!;

        Assert.Equal("t", update.MetaTitle);
        Assert.Equal("to", update.Title);
        Assert.Equal("ot", update.MetaOriginalTitle);
        Assert.Equal("d", update.MetaDescription);
        Assert.Equal(new DateTime(2020, 5, 6), update.MetaReleaseDate);
        Assert.Equal("s", update.MetaStudio);
        Assert.Equal("l", update.MetaLabel);
        Assert.Equal("se", update.MetaSeries);
        Assert.Equal("di", update.MetaDirector);
        Assert.Equal(MovieContentType.Decensored, update.ContentType);
    }

    [Fact]
    public void Apply_Clear_NullsOnlyThoseFields_CaseInsensitively()
    {
        var result = MovieMetadataPatch.Apply(Current(), new MetadataPatchRequest(Clear: ["TITLEOVERRIDE", "releaseDate", "Studio"]));

        var update = result.Value!;
        Assert.Null(update.Title);
        Assert.Null(update.MetaReleaseDate);
        Assert.Null(update.MetaStudio);
        Assert.Equal("Meta", update.MetaTitle);
        Assert.Equal("Label", update.MetaLabel);
    }

    [Fact]
    public void Apply_SetAndClearDifferentFields_Combine()
    {
        var result = MovieMetadataPatch.Apply(Current(), new MetadataPatchRequest(new MetadataFields(Series: "New"), ["label"]));

        Assert.Equal("New", result.Value!.MetaSeries);
        Assert.Null(result.Value.MetaLabel);
    }

    [Fact]
    public void Apply_EmptyPatch_IsInvalid()
    {
        Assert.Equal(ApiStatus.Invalid, MovieMetadataPatch.Apply(Current(), new MetadataPatchRequest()).Status);
        Assert.Equal(ApiStatus.Invalid, MovieMetadataPatch.Apply(Current(), new MetadataPatchRequest(new MetadataFields(), [])).Status);
    }

    [Fact]
    public void Apply_UnknownClearName_IsInvalid()
    {
        var result = MovieMetadataPatch.Apply(Current(), new MetadataPatchRequest(Clear: ["runtime"]));

        Assert.Equal(ApiStatus.Invalid, result.Status);
        Assert.Contains("runtime", result.Errors[0]);
    }

    [Fact]
    public void Apply_ContentTypeIsNotClearable()
    {
        Assert.Equal(ApiStatus.Invalid, MovieMetadataPatch.Apply(Current(), new MetadataPatchRequest(Clear: ["contentType"])).Status);
    }

    [Fact]
    public void Apply_FieldInSetAndClear_IsInvalid()
    {
        var result = MovieMetadataPatch.Apply(Current(), new MetadataPatchRequest(new MetadataFields(Studio: "X", ReleaseDate: new DateOnly(2020, 1, 1)), ["studio", "releaseDate"]));

        Assert.Equal(ApiStatus.Invalid, result.Status);
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void Apply_WhitespaceValue_IsInvalid_PointingAtClear()
    {
        var result = MovieMetadataPatch.Apply(Current(), new MetadataPatchRequest(new MetadataFields(Description: "   ")));

        Assert.Equal(ApiStatus.Invalid, result.Status);
        Assert.Contains("clear", result.Errors[0]);
    }

    [Theory]
    [InlineData("censored", MovieContentType.Censored)]
    [InlineData("UNCENSORED", MovieContentType.Uncensored)]
    [InlineData(" Decensored ", MovieContentType.Decensored)]
    public void Apply_ContentType_AcceptsTheThreeNames(string value, MovieContentType expected)
    {
        var result = MovieMetadataPatch.Apply(Current(), new MetadataPatchRequest(new MetadataFields(ContentType: value)));

        Assert.Equal(expected, result.Value!.ContentType);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("mosaic")]
    [InlineData("Censored, Uncensored")]
    public void Apply_ContentType_RejectsAnythingElse(string value)
    {
        var result = MovieMetadataPatch.Apply(Current(), new MetadataPatchRequest(new MetadataFields(ContentType: value)));

        Assert.Equal(ApiStatus.Invalid, result.Status);
    }
}
