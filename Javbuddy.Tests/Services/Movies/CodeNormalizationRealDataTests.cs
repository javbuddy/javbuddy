using Javbuddy.Services.Movies;

namespace Javbuddy.Tests.Services.Movies;

/// <summary>Characterizes CodeNormalization's distributor-detection/dedup logic against real
/// dvd_id values pulled from the actual r18.dev dump (r18dotdev_dump_2026-08-25.sql.gz,
/// derived_video table, ~1.53M rows) rather than hand-crafted examples — complements
/// CodeNormalizationTests, which covers the documented synthetic cases. Extracted with a
/// throwaway tool that streamed the real dump through the production DumpParser/
/// CodeNormalization classes themselves (not re-implemented), so every fixture here is a real
/// release code and, for the dedup groups, a real score computed from the real poster/title
/// flags r18.dev has on that row.</summary>
public class CodeNormalizationRealDataTests
{
    // ---- IsDistributorRelease: real codes that should be flagged ----

    [Theory]
    // Leading digit run (e.g. a numeric "100TV" label prefix) — matches even for a 3-digit run,
    // not just the single-digit examples in the class's own doc comment.
    [InlineData("100TV-031")]
    [InlineData("3DBD-003")]
    // TK/PB/RS/KS prefix, 4+ leading letters.
    [InlineData("TKBT-09")]
    [InlineData("KSBE-006")]
    [InlineData("KSBT-002")]
    [InlineData("KSDO-010")]
    // Explicit trailing distributor word.
    [InlineData("JOB-019DOD")]
    [InlineData("DIC-008DOD")]
    [InlineData("ONE-009DOD")]
    // Trailing digits + a single uppercase letter — caught by the suffix regex's broad "digits
    // then one [A-Z]" catch-all, not just the named distributor words (BOD/DOD/etc). Real
    // examples: "R"-suffixed reissues and "B"-suffixed alternate cuts.
    [InlineData("UD321R")]
    [InlineData("UD-330R")]
    [InlineData("SS-006B")]
    [InlineData("SS-092B")]
    public void IsDistributorRelease_RealFlaggedCodes(string dvdId)
    {
        Assert.True(CodeNormalization.IsDistributorRelease(dvdId));
    }

    // ---- IsDistributorRelease: real codes that should NOT be flagged ----

    [Theory]
    [InlineData("TV-95")]
    [InlineData("ARMD-467")]
    [InlineData("ATMD-03")]
    [InlineData("ATMD-003")]
    [InlineData("BSHD-09")]
    [InlineData("VRAT004")]
    [InlineData("BTCD-02")]
    [InlineData("FSMD-29")]
    [InlineData("HIS-004")]
    [InlineData("PEMD-18")]
    [InlineData("ARMG-186")]
    [InlineData("PRDVR-022")]
    [InlineData("DMM-070802")]
    [InlineData("FTA-182")]
    // The precise real-world boundary case for the "TK/PB/RS/KS needs 4+ leading letters" rule:
    // "KSL" is only 3 letters, so this does NOT match despite starting with "KS" — contrast with
    // "KSBE-006"/"KSBT-002"/"KSDO-010" above, all 4-letter prefixes, which do.
    [InlineData("KSL-10")]
    public void IsDistributorRelease_RealNonFlaggedCodes(string dvdId)
    {
        Assert.False(CodeNormalization.IsDistributorRelease(dvdId));
    }

    // ---- GetCanonicalKey: real variants of the same release group together ----

    public static TheoryData<string[]> RealDuplicateGroups()
    {
        // Each group: real dvd_id variants of what r18.dev itself lists as (data-entry/
        // distributor-re-release) forms of the same release, taken verbatim from the dump.
        var data = new TheoryData<string[]>
        {
            (["JOB-019", "JOB-19", "JOB-019DOD", "JOB019"]),
            (["SS-006", "RSSS006", "SS006", "SS-006B"]),
            (["SS-007", "RSSS007", "SS007", "SS-007B"]),
            (["SSD-10", "SSD-010", "SSD010", "SSD-000010"]),
            (["WWD-01", "WWD-000001", "WWD001", "WWD-001"]),
            (["MKD-02", "MKD-00002", "MKD002", "MKD-2"]),
            (["NTRD-002", "2NTRD002", "7NTRD-002", "NTRD-002DOD"]),
            (["STAR-010", "STAR-10", "STAR-000010", "STAR-010DOD"]),
            (["HID-004", "HID004", "HID-04", "HID-4"]),
            (["DWD-002", "DWD-02", "DWD-00002", "DWD002"])
        };
        return data;
    }

    [Theory]
    [MemberData(nameof(RealDuplicateGroups))]
    public void GetCanonicalKey_RealDuplicateVariants_AllShareOneCanonicalKey(string[] variants)
    {
        var canonicalKeys = variants.Select(CodeNormalization.GetCanonicalKey).Distinct().ToList();

        var onlyKey = Assert.Single(canonicalKeys);
        Assert.NotEmpty(onlyKey);
    }

    // ---- ScoreDvdIdForCanonical: real dedup groups pick the expected "cleanest" variant ----
    //
    // (dvd_id, hasPoster, hasTitle, expectedWinner) — poster/title flags are the real values
    // read off that row in the dump, since the score weighs them in. Each group's variants all
    // had a poster and a title in the real data, so the winner is driven entirely by the
    // code-shape rules (distributor prefix/suffix, hyphenation, zero-padding, length).

    public static TheoryData<string[], string> RealScoredGroups()
    {
        var data = new TheoryData<string[], string>
        {
            { ["JOB-019", "JOB-19", "JOB-019DOD", "JOB019"], "JOB-19" },
            { ["SS-006", "RSSS006", "SS006", "SS-006B"], "SS-006" },
            { ["SS-007", "RSSS007", "SS007", "SS-007B"], "SS-007" },
            { ["SSD-10", "SSD-010", "SSD010", "SSD-000010"], "SSD-10" },
            { ["WWD-01", "WWD-000001", "WWD001", "WWD-001"], "WWD-01" },
            { ["MKD-02", "MKD-00002", "MKD002", "MKD-2"], "MKD-2" },
            { ["NTRD-002", "2NTRD002", "7NTRD-002", "NTRD-002DOD"], "NTRD-002" },
            { ["STAR-010", "STAR-10", "STAR-000010", "STAR-010DOD"], "STAR-10" },
            { ["HID-004", "HID004", "HID-04", "HID-4"], "HID-4" },
            { ["DWD-002", "DWD-02", "DWD-00002", "DWD002"], "DWD-02" }
        };
        return data;
    }

    [Theory]
    [MemberData(nameof(RealScoredGroups))]
    public void ScoreDvdIdForCanonical_RealDuplicateGroup_PrefersExpectedVariant(string[] variants, string expectedWinner)
    {
        var winner = variants
            .OrderByDescending(v => CodeNormalization.ScoreDvdIdForCanonical(v, hasPoster: true, hasTitle: true))
            .First();

        Assert.Equal(expectedWinner, winner);
    }

    // ---- A real multi-disc set: same canonical key, but these are genuinely different discs,
    // not duplicates — documents a real limitation rather than asserting it's "correct". ----

    [Fact]
    public void GetCanonicalKey_RealMultiDiscSet_CollapsesToOneCanonicalKey()
    {
        // GRCH-315-1 through -4 are four discs of the same boxed set on r18.dev, not re-release
        // variants of one movie — but GetCanonicalKey has no way to tell "-1/-2/-3/-4" apart from
        // a distributor suffix, so ActorMissing's hideDuplicates would only ever show one of them.
        string[] discs = ["GRCH-315-1", "GRCH-315-2", "GRCH-315-3", "GRCH-315-4"];

        var canonicalKeys = discs.Select(CodeNormalization.GetCanonicalKey).Distinct().ToList();

        Assert.Single(canonicalKeys);
    }
}
