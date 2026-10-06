using Javbuddy.Services.ActorEnrichment;

namespace Javbuddy.Tests.Services.ActorEnrichment;

public class LibraryCastIndexTests
{
    [Fact]
    public void CodesCasting_ReturnsEachMatchingMovieOnce_InLibraryOrder_IgnoringCase()
    {
        var index = new LibraryCastIndex([
            ("CCC-3", "Mikami Yua, Other Actress"),
            ("AAA-1", "Someone Else"),
            ("BBB-2", "mikami yua、三上悠亜"),
            ("DDD-4", null)
        ]);

        var codes = index.CodesCasting(["三上悠亜", "MIKAMI YUA", "Mikami Yua"]);

        Assert.Equal(["CCC-3", "BBB-2"], codes);
    }

    [Fact]
    public void CodesCasting_UnknownNames_ReturnsEmpty()
    {
        var index = new LibraryCastIndex([("AAA-1", "Someone Else")]);

        Assert.Empty(index.CodesCasting(["Nobody"]));
    }
}
