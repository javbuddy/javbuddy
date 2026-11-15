using System.Text.Json.Serialization;

namespace Javbuddy.Services.DeoVr;

// DeoVR's JSON API (https://deovr.com/documentation): the /deovr list document and one video's
// document. Property names are DeoVR's own, mixed casing included.

public sealed record DeoVrScenesResponse(
    [property: JsonPropertyName("scenes")] IReadOnlyList<DeoVrScene> Scenes);

public sealed record DeoVrScene(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("list")] IReadOnlyList<DeoVrListItem> List);

/// <summary>A video in a list, in DeoVR's shortened form: the full document is at VideoUrl.</summary>
public sealed record DeoVrListItem(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("videoLength")] int VideoLength,
    [property: JsonPropertyName("video_url")] string VideoUrl,
    [property: JsonPropertyName("thumbnailUrl")] string ThumbnailUrl);

/// <summary>One video's document. TimelinePreview, the seek-bar preview mosaic, isn't in
/// DeoVR's published docs but is what DeoVR's own documents use; it's left out when there's none.</summary>
public sealed record DeoVrVideoResponse(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("videoLength")] int VideoLength,
    [property: JsonPropertyName("screenType")] string ScreenType,
    [property: JsonPropertyName("stereoMode")] string StereoMode,
    [property: JsonPropertyName("thumbnailUrl")] string ThumbnailUrl,
    [property: JsonPropertyName("encodings")] IReadOnlyList<DeoVrEncoding> Encodings,
    [property: JsonPropertyName("timeStamps")] IReadOnlyList<DeoVrTimestamp> TimeStamps,
    [property: JsonPropertyName("timelinePreview"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TimelinePreview = null,
    [property: JsonPropertyName("description"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description = null,
    [property: JsonPropertyName("date"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? Date = null,
    [property: JsonPropertyName("paysite"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DeoVrNamed? Paysite = null,
    [property: JsonPropertyName("actors"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<DeoVrNamed>? Actors = null,
    [property: JsonPropertyName("categories"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<DeoVrCategory>? Categories = null,
    [property: JsonPropertyName("isFavorite"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IsFavorite = null,
    [property: JsonPropertyName("subtitles"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<DeoVrSubtitle>? Subtitles = null)
{
    /// <summary>Always true: DeoVR's docs say false forces every video to play monoscopic. Flat and
    /// mono video are described by screenType "flat" and stereoMode "off" instead.</summary>
    [JsonPropertyName("is3d")]
    public bool Is3D => true;
}

public sealed record DeoVrEncoding(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("videoSources")] IReadOnlyList<DeoVrVideoSource> VideoSources);

public sealed record DeoVrVideoSource(
    [property: JsonPropertyName("resolution")] int Resolution,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("width"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Width = null,
    [property: JsonPropertyName("height"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Height = null,
    [property: JsonPropertyName("size"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? Size = null);

public sealed record DeoVrNamed(
    [property: JsonPropertyName("name")] string Name);

public sealed record DeoVrCategory(
    [property: JsonPropertyName("tag")] DeoVrNamed Tag);

public sealed record DeoVrSubtitle(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("language"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Language,
    [property: JsonPropertyName("url")] string Url);

/// <summary>A chapter: its start in whole seconds and its name.</summary>
public sealed record DeoVrTimestamp(
    [property: JsonPropertyName("ts")] int Ts,
    [property: JsonPropertyName("name")] string Name);
