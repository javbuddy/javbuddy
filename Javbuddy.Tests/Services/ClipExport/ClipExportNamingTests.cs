using Javbuddy.Services.ClipExport;

namespace Javbuddy.Tests.Services.ClipExport;

public class ClipExportNamingTests
{
    [Fact]
    public void FileName_CodeLabelAndRange_WithoutColons()
    {
        Assert.Equal("MIDE-400 - Scene 2 [00-12-31 - 01-02-03].mp4", ClipExportNaming.FileName("MIDE-400", "Scene 2", 751.9, 3723, ".mp4"));
    }

    [Theory]
    [InlineData(0, "00-00-00")]
    [InlineData(59.99, "00-00-59")]
    [InlineData(3600, "01-00-00")]
    [InlineData(-3, "00-00-00")]
    public void FormatTime_RoundsDown_ToWholeSeconds(double seconds, string expected)
    {
        Assert.Equal(expected, ClipExportNaming.FormatTime(seconds));
    }

    [Theory]
    [InlineData("a/b:c*d?", "a_b_c_d_")]
    [InlineData("  two   spaces\there  ", "two spaces here")]
    [InlineData("ends with dots...", "ends with dots")]
    [InlineData("", "clip")]
    [InlineData(null, "clip")]
    [InlineData("...", "clip")]
    [InlineData("日本語 タイトル", "日本語 タイトル")]
    public void Sanitize_ReplacesReservedCharacters_AndTrims(string? input, string expected)
    {
        Assert.Equal(expected, ClipExportNaming.Sanitize(input));
    }

    [Fact]
    public void Sanitize_CapsTheUtf8Length_WithoutSplittingCharacters()
    {
        Assert.Equal(ClipExportNaming.MaxLabelBytes, ClipExportNaming.Sanitize(new string('x', 500)).Length);

        var japanese = ClipExportNaming.Sanitize(new string('あ', 200));
        Assert.Equal(ClipExportNaming.MaxLabelBytes / 3, japanese.Length);
        Assert.All(japanese, c => Assert.Equal('あ', c));

        var emoji = ClipExportNaming.Sanitize(string.Concat(Enumerable.Repeat("😀", 100)));
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(emoji) <= ClipExportNaming.MaxLabelBytes);
        Assert.False(char.IsHighSurrogate(emoji[^1]));
    }

    [Fact]
    public void FileName_OfALongJapaneseTitle_FitsIn255Bytes()
    {
        var name = ClipExportNaming.FileName("ABCD-1234", new string('あ', 200), 3600, 7200, ".webm");

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(name) <= 255);
    }
}
