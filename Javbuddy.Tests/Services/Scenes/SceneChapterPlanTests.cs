using Javbuddy.Models;
using Javbuddy.Services.Scenes;

namespace Javbuddy.Tests.Services.Scenes;

public class SceneChapterPlanTests
{
    private static IReadOnlyList<ResolvedScene> Scenes(double? duration, params (double Start, double? End, string? Title)[] scenes) =>
        SceneRanges.ResolveEffectiveRanges(
            scenes.Select((s, i) => new Scene { Id = i + 1, StartSeconds = s.Start, EndSeconds = s.End, Title = s.Title }),
            duration);

    [Fact]
    public void Build_ContiguousScenesFromZero_NoGaps()
    {
        var plan = SceneChapterPlan.Build(Scenes(100, (0, null, "A"), (40, null, "B")), 100);

        Assert.Equal([new PlannedChapter(0, 40, "A"), new PlannedChapter(40, 100, "B")], plan);
    }

    [Fact]
    public void Build_GapsBeforeBetweenAndAfter_BecomeUntitledChapters()
    {
        var plan = SceneChapterPlan.Build(Scenes(100, (10, 30, "Interview"), (50, 80, null)), 100);

        Assert.Equal(
            [
                new PlannedChapter(0, 10, null),
                new PlannedChapter(10, 30, "Interview"),
                new PlannedChapter(30, 50, null),
                new PlannedChapter(50, 80, "Scene 2"),
                new PlannedChapter(80, 100, null),
            ],
            plan);
    }

    [Fact]
    public void Build_SliversUnderHalfASecond_AreAbsorbed()
    {
        var plan = SceneChapterPlan.Build(Scenes(100, (0.2, 30, "A"), (30.3, 99.8, "B")), 100);

        Assert.Equal([new PlannedChapter(0, 30.3, "A"), new PlannedChapter(30.3, 100, "B")], plan);
    }

    [Fact]
    public void Build_ClampsToTheFile_AndDropsScenesPastIt()
    {
        // Movie duration (MediaInfo) says 120, but the file itself is 100 s long.
        var plan = SceneChapterPlan.Build(Scenes(120, (0, 90, "A"), (90, null, "B"), (110, null, "C")), 100);

        Assert.Equal([new PlannedChapter(0, 90, "A"), new PlannedChapter(90, 100, "B")], plan);
    }

    [Fact]
    public void ToFfMetadata_Milliseconds_BlankGapTitleOnlyWhenAsked_EscapesTitles()
    {
        IReadOnlyList<PlannedChapter> chapters = [new(0, 1.5, null), new(1.5, 6, "Bath; = #1")];

        var mp4 = SceneChapterPlan.ToFfMetadata(chapters, blankTitleForGaps: true);
        var mkv = SceneChapterPlan.ToFfMetadata(chapters, blankTitleForGaps: false);

        Assert.Equal(";FFMETADATA1\n[CHAPTER]\nTIMEBASE=1/1000\nSTART=0\nEND=1500\ntitle= \n[CHAPTER]\nTIMEBASE=1/1000\nSTART=1500\nEND=6000\ntitle=Bath\\; \\= \\#1\n", mp4);
        Assert.DoesNotContain("title= \n", mkv);
    }

    [Theory]
    [InlineData("a.mp4", true)]
    [InlineData("a.M4V", true)]
    [InlineData("a.mov", true)]
    [InlineData("a.mkv", false)]
    [InlineData("a.webm", false)]
    public void IsMp4Family(string path, bool expected) =>
        Assert.Equal(expected, SceneChapterPlan.IsMp4Family(path));
}
