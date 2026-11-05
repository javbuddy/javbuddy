using Javbuddy.Services.Movies;
using NSubstitute;

namespace Javbuddy.Tests.Services.Movies;

public sealed class SubtitleFilesTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("subs").FullName;

    public void Dispose() => Directory.Delete(dir, true);

    private string Touch(string name, string content = "")
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Find_MatchesBaseNameWithOptionalSuffix_AndIgnoresOthers()
    {
        var video = Touch("ABC-123.mp4");
        Touch("ABC-123.srt");
        Touch("ABC-123.en.srt");
        Touch("abc-123.ja.VTT");
        Touch("ABC-1234.srt");
        Touch("ABC-123.ass");
        Touch("Other.srt");

        var found = SubtitleFiles.Find(video);

        Assert.Equal(["English", "Japanese", "Subtitles"], found.Select(f => f.Label));
        Assert.Equal(["en", "ja", null], found.Select(f => f.Language));
        Assert.Equal([0, 1, 2], found.Select(f => f.Index));
    }

    [Fact]
    public void Find_NoSubtitles_ReturnsEmpty() => Assert.Empty(SubtitleFiles.Find(Touch("a.mp4")));

    [Fact]
    public void Label_UnknownSuffix_IsKeptAsWritten() => Assert.Equal("forced track", SubtitleFiles.Label("forced track"));

    [Fact]
    public void ToWebVtt_Srt_GetsHeaderAndPointTimestamps()
    {
        var path = Touch("a.srt", "﻿1\r\n00:00:01,500 --> 00:00:03,000\r\nHello, world\r\n");

        Assert.Equal("WEBVTT\n\n1\n00:00:01.500 --> 00:00:03.000\nHello, world\n", SubtitleFiles.ToWebVtt(path));
    }

    [Fact]
    public void ToWebVtt_Srt_EscapesMarkupAndPadsShortMilliseconds()
    {
        var path = Touch("a.srt", "1\n00:00:01,5 --> 00:00:02,25\nTom & Jerry <3 <i>you</i> <font color=\"red\">x</font>\n");

        Assert.Equal("WEBVTT\n\n1\n00:00:01.500 --> 00:00:02.250\nTom &amp; Jerry &lt;3 <i>you</i> x\n", SubtitleFiles.ToWebVtt(path));
    }

    [Fact]
    public void ToWebVtt_ReadsShiftJisAndUtf16Files()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var cue = "1\n00:00:01,000 --> 00:00:02,000\nこんにちは\n";
        var sjis = Path.Combine(dir, "sjis.srt");
        File.WriteAllBytes(sjis, System.Text.Encoding.GetEncoding(932).GetBytes(cue));
        var utf16 = Path.Combine(dir, "utf16.srt");
        File.WriteAllBytes(utf16, [.. System.Text.Encoding.Unicode.GetPreamble(), .. System.Text.Encoding.Unicode.GetBytes(cue)]);

        Assert.Contains("こんにちは", SubtitleFiles.ToWebVtt(sjis));
        Assert.Contains("こんにちは", SubtitleFiles.ToWebVtt(utf16));
    }

    [Fact]
    public void ToWebVtt_Vtt_PassesThrough()
    {
        var vtt = "WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nHi\n";

        Assert.Equal(vtt, SubtitleFiles.ToWebVtt(Touch("a.vtt", vtt)));
    }

    [Fact]
    public async Task Service_ListsTracksAndServesByIndex_OnlyBesideTheResolvedFile()
    {
        var video = Touch("m.mp4");
        Touch("m.en.srt", "1\n00:00:01,000 --> 00:00:02,000\nHi\n");
        var streams = Substitute.For<IMovieStreamService>();
        streams.GetMainFilePathAsync(1, Arg.Any<CancellationToken>()).Returns(video);
        var service = new MovieSubtitleService(streams);

        var tracks = await service.GetTracksAsync(1);

        var track = Assert.Single(tracks);
        Assert.Equal("/api/movies/1/subtitles/0", track.Url);
        Assert.Contains("00:00:01.000 --> 00:00:02.000", await service.GetWebVttAsync(1, null, 0));
        Assert.Null(await service.GetWebVttAsync(1, null, 5));
        Assert.Empty(await service.GetTracksAsync(2));
    }

    [Fact]
    public async Task Service_ForAVersion_UsesThatFilesSiblings_AndItsUrlsCarryTheFileId()
    {
        var video = Touch("m-4k.mp4");
        Touch("m-4k.srt", "1\n00:00:01,000 --> 00:00:02,000\nHi\n");
        var streams = Substitute.For<IMovieStreamService>();
        streams.GetFilePathAsync(1, 7, Arg.Any<CancellationToken>()).Returns(video);
        var service = new MovieSubtitleService(streams);

        var track = Assert.Single(await service.GetTracksAsync(1, 7));

        Assert.Equal("/api/movies/1/subtitles/0?fileId=7", track.Url);
        Assert.NotNull(await service.GetWebVttAsync(1, 7, 0));
    }
}
