using System.Xml.Linq;
using Javbuddy.Models;
using Javbuddy.Services.Nfo;

namespace Javbuddy.Tests.Services.Nfo;

/// <summary>Creating the &lt;title&gt; of an .nfo that lacks one (or has it empty).</summary>
public class NfoFieldSyncTitleTests
{
    private static Movie MovieWithTitle(string? title) => new() { Code = "ABC-123", MetaTitle = title };

    private static XElement Load(string xml) => XDocument.Parse(xml, LoadOptions.PreserveWhitespace).Root!;

    [Fact]
    public void MissingTitle_IsInsertedAfterId_WithTheFilesIndentation()
    {
        var root = Load("<movie>\n  <id>ABC-123</id>\n  <plot>P</plot>\n</movie>");

        Assert.True(NfoFieldSync.ApplyFieldUpdates(MovieWithTitle("New"), root));
        Assert.Equal("<movie>\n  <id>ABC-123</id>\n  <title>New</title>\n  <plot>P</plot>\n</movie>", root.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void MissingTitle_WithoutId_BecomesTheFirstChild()
    {
        var root = Load("<movie>\n  <plot>P</plot>\n</movie>");

        Assert.True(NfoFieldSync.ApplyFieldUpdates(MovieWithTitle("New"), root));
        Assert.Equal("<movie>\n  <title>New</title>\n  <plot>P</plot>\n</movie>", root.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void EmptyRoot_GetsTheTitle()
    {
        var root = Load("<movie />");

        Assert.True(NfoFieldSync.ApplyFieldUpdates(MovieWithTitle("New"), root));
        Assert.Equal("New", root.Element("title")!.Value);
    }

    [Fact]
    public void EmptyTitleElement_IsFilledInPlace_NotDuplicated()
    {
        var root = Load("<movie>\n  <title></title>\n  <plot>P</plot>\n</movie>");

        Assert.True(NfoFieldSync.ApplyFieldUpdates(MovieWithTitle("New"), root));
        Assert.Equal(["New"], root.Elements("title").Select(e => e.Value));
    }

    [Fact]
    public void MatchingTitle_ReportsNoChange()
    {
        var root = Load("<movie><title>Same</title></movie>");

        Assert.False(NfoFieldSync.ApplyFieldUpdates(MovieWithTitle("Same"), root));
    }

    [Fact]
    public void NoCanonicalTitle_DoesNotInventAnElement()
    {
        var root = Load("<movie><plot>P</plot></movie>");

        Assert.False(NfoFieldSync.ApplyFieldUpdates(MovieWithTitle(null), root));
        Assert.Null(root.Element("title"));
    }

    [Fact]
    public void WhitespaceOnlyCanonicalTitle_DoesNotInventAnElement()
    {
        var root = Load("<movie><plot>P</plot></movie>");

        Assert.False(NfoFieldSync.ApplyFieldUpdates(MovieWithTitle("   "), root));
        Assert.Null(root.Element("title"));
    }

    [Fact]
    public void WithoutPreservedWhitespace_TitleIsInsertedAfterId()
    {
        var root = XDocument.Parse("<movie><id>ABC-123</id><plot>P</plot></movie>").Root!;

        Assert.True(NfoFieldSync.ApplyFieldUpdates(MovieWithTitle("New"), root));
        Assert.Equal("<movie><id>ABC-123</id><title>New</title><plot>P</plot></movie>", root.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void WithoutPreservedWhitespace_TitleBecomesTheFirstChildWhenThereIsNoId()
    {
        var root = XDocument.Parse("<movie><plot>P</plot></movie>").Root!;

        Assert.True(NfoFieldSync.ApplyFieldUpdates(MovieWithTitle("New"), root));
        Assert.Equal("<movie><title>New</title><plot>P</plot></movie>", root.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void TitleWithSurroundingWhitespace_MatchingTheCanonicalTitle_ReportsNoChange()
    {
        var root = Load("<movie><title> Same </title></movie>");

        Assert.False(NfoFieldSync.ApplyFieldUpdates(MovieWithTitle("Same"), root));
    }
}
