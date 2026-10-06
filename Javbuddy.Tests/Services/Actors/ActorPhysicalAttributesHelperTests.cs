using Javbuddy.Models;
using Javbuddy.Services.Actors;

namespace Javbuddy.Tests.Services.Actors;

public class ActorPhysicalAttributesHelperTests
{
    [Fact]
    public void CalculateAgeAtRelease_ReleaseDateKnown_ReturnsAgeOnReleaseDate()
    {
        var age = ActorPhysicalAttributesHelper.CalculateAgeAtRelease(
            new DateTime(1993, 8, 16), new DateTime(2017, 8, 15));
        Assert.Equal(23, age);
    }

    [Fact]
    public void CalculateAgeAtRelease_ReleaseDateUnknown_ReturnsCurrentAge()
    {
        var birthDate = new DateTime(1993, 8, 16);
        Assert.Equal(
            ActorPhysicalAttributesHelper.CalculateAge(birthDate),
            ActorPhysicalAttributesHelper.CalculateAgeAtRelease(birthDate, null));
    }

    [Fact]
    public void CalculateAgeAtRelease_BirthDateUnknown_ReturnsNull()
    {
        Assert.Null(ActorPhysicalAttributesHelper.CalculateAgeAtRelease(null, new DateTime(2017, 8, 15)));
    }

    [Theory]
    [InlineData(1993, 8, 16, 2026, 8, 16, true)]
    [InlineData(1993, 8, 16, 2026, 8, 15, false)]
    [InlineData(1993, 8, 16, 2026, 8, 17, false)]
    [InlineData(1996, 2, 29, 2028, 2, 29, true)]
    [InlineData(1996, 2, 29, 2027, 2, 28, true)]
    [InlineData(1996, 2, 29, 2027, 3, 1, false)]
    [InlineData(1996, 2, 29, 2028, 2, 28, false)]
    public void IsBirthday_MatchesMonthAndDay_WithLeapDayOnFeb28InNonLeapYears(
        int birthYear, int birthMonth, int birthDay, int year, int month, int day, bool expected)
    {
        Assert.Equal(expected, ActorPhysicalAttributesHelper.IsBirthday(
            new DateTime(birthYear, birthMonth, birthDay), new DateOnly(year, month, day)));
    }

    [Fact]
    public void IsBirthday_BirthDateUnknown_ReturnsFalse()
    {
        Assert.False(ActorPhysicalAttributesHelper.IsBirthday(null, new DateOnly(2026, 8, 16)));
    }

    [Theory]
    [InlineData(157, "157 cm")]
    [InlineData(180, "180 cm")]
    [InlineData(150, "150 cm")]
    public void FormatHeight_ValidHeight_ReturnsMetric(int heightCm, string expected)
    {
        Assert.Equal(expected, ActorPhysicalAttributesHelper.FormatHeight(heightCm));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-5)]
    public void FormatHeight_NullOrNonPositive_ReturnsNull(int? heightCm)
    {
        Assert.Null(ActorPhysicalAttributesHelper.FormatHeight(heightCm));
    }

    [Fact]
    public void FormatMeasurements_AllThreePopulated_ReturnsHyphenatedMetric()
    {
        var result = ActorPhysicalAttributesHelper.FormatMeasurements(89, 65, 94);
        Assert.Equal("89-65-94", result);
    }

    [Fact]
    public void FormatMeasurements_BustOnly_ReturnsFormattedBust()
    {
        var result = ActorPhysicalAttributesHelper.FormatMeasurements(89, null, null);
        Assert.Equal("89", result);
    }

    [Fact]
    public void FormatMeasurements_BustAndWaist_ReturnsFormattedPair()
    {
        var result = ActorPhysicalAttributesHelper.FormatMeasurements(89, 65, null);
        Assert.Equal("89-65", result);
    }

    [Fact]
    public void FormatMeasurements_AllNullOrZero_ReturnsNull()
    {
        Assert.Null(ActorPhysicalAttributesHelper.FormatMeasurements(null, null, null));
        Assert.Null(ActorPhysicalAttributesHelper.FormatMeasurements(0, 0, 0));
    }

    [Theory]
    [InlineData("c", "C")]
    [InlineData(" C ", "C")]
    [InlineData("dd", "DD")]
    [InlineData("g", "G")]
    public void NormalizeCupSize_ValidCup_ReturnsUppercase(string input, string expected)
    {
        Assert.Equal(expected, ActorPhysicalAttributesHelper.NormalizeCupSize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeCupSize_NullOrWhitespace_ReturnsNull(string? input)
    {
        Assert.Null(ActorPhysicalAttributesHelper.NormalizeCupSize(input));
    }

    [Theory]
    [InlineData(" c ", "C")]
    [InlineData("k", "K")]
    [InlineData("DD", null)]
    [InlineData("L", null)]
    [InlineData("unknown", null)]
    [InlineData(null, null)]
    public void StandardCupSizeOrNull_KeepsOnlyStandardSizes(string? input, string? expected)
    {
        Assert.Equal(expected, ActorPhysicalAttributesHelper.StandardCupSizeOrNull(input));
    }

    [Theory]
    [InlineData("89-65-94", 89, 65, 94)]
    [InlineData("B89 W65 H94", 89, 65, 94)]
    [InlineData("89/65/94", 89, 65, 94)]
    [InlineData("B89-W65-H94", 89, 65, 94)]
    public void TryParseMeasurements_ValidStrings_ParsesValues(string input, int expectedBust, int expectedWaist, int expectedHips)
    {
        var success = ActorPhysicalAttributesHelper.TryParseMeasurements(input, out var b, out var w, out var h);
        Assert.True(success);
        Assert.Equal(expectedBust, b);
        Assert.Equal(expectedWaist, w);
        Assert.Equal(expectedHips, h);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not measurements")]
    [InlineData("89")]
    public void TryParseMeasurements_Invalid_ReturnsFalse(string? input)
    {
        var success = ActorPhysicalAttributesHelper.TryParseMeasurements(input, out var b, out var w, out var h);
        Assert.False(success);
        Assert.Null(b);
        Assert.Null(w);
        Assert.Null(h);
    }

    [Fact]
    public void JapaneseCupSizeExplanation_MatchesSpec()
    {
        Assert.Contains("Japanese cup sizes differ from European standards", ActorPhysicalAttributesHelper.JapaneseCupSizeExplanation);
        Assert.Contains("padded bra", ActorPhysicalAttributesHelper.JapaneseCupSizeExplanation);
    }

    [Theory]
    [InlineData("1999-11-29", "2026-09-21", 26)] // example
    [InlineData("1999-09-21", "2026-09-21", 27)] // Birthday today
    [InlineData("1999-09-22", "2026-09-21", 26)] // Birthday tomorrow
    [InlineData("1999-09-20", "2026-09-21", 27)] // Birthday yesterday
    [InlineData("1999-01-01", "2026-09-21", 27)] // Birthday earlier in year
    [InlineData("1999-12-31", "2026-09-21", 26)] // Birthday later in year
    [InlineData("2026-09-21", "2026-09-21", 0)]  // Born today
    public void CalculateAge_VariousDates_ReturnsExpectedAge(string birthDateStr, string asOfStr, int expectedAge)
    {
        var birthDate = DateTime.Parse(birthDateStr);
        var asOf = DateOnly.Parse(asOfStr);

        var age = ActorPhysicalAttributesHelper.CalculateAge(birthDate, asOf);

        Assert.Equal(expectedAge, age);
    }

    [Theory]
    [InlineData("2024-02-28", 23)] // Leap year, day before leap day
    [InlineData("2024-02-29", 24)] // Leap year, on leap day
    [InlineData("2025-02-28", 24)] // Non-leap year, Feb 28
    [InlineData("2025-03-01", 25)] // Non-leap year, March 1
    public void CalculateAge_LeapYearFeb29_HandlesLeapAndNonLeapYears(string asOfStr, int expectedAge)
    {
        var birthDate = new DateTime(2000, 2, 29);
        var asOf = DateOnly.Parse(asOfStr);

        var age = ActorPhysicalAttributesHelper.CalculateAge(birthDate, asOf);

        Assert.Equal(expectedAge, age);
    }

    [Fact]
    public void CalculateAge_NullOrFuture_ReturnsNull()
    {
        Assert.Null(ActorPhysicalAttributesHelper.CalculateAge(null));

        var future = new DateTime(2030, 1, 1);
        var asOf = new DateOnly(2026, 9, 21);
        Assert.Null(ActorPhysicalAttributesHelper.CalculateAge(future, asOf));
    }

    [Fact]
    public void FormatBirthDate_ValidBirthDate_FormatsEuropeanStandardWithAge()
    {
        // exact example: 29.11.1999 (26)
        var birthDate = new DateTime(1999, 11, 29);
        var asOf = new DateOnly(2026, 9, 21);

        var formatted = ActorPhysicalAttributesHelper.FormatBirthDate(birthDate, asOf);

        Assert.Equal("29.11.1999 (26)", formatted);
    }

    [Fact]
    public void FormatBirthDate_BirthdayToday_FormatsWithAge()
    {
        var birthDate = new DateTime(1999, 9, 21);
        var asOf = new DateOnly(2026, 9, 21);

        var formatted = ActorPhysicalAttributesHelper.FormatBirthDate(birthDate, asOf);

        Assert.Equal("21.09.1999 (27)", formatted);
    }

    [Fact]
    public void FormatBirthDate_FutureDate_FormatsWithoutAge()
    {
        var birthDate = new DateTime(2030, 1, 5);
        var asOf = new DateOnly(2026, 9, 21);

        var formatted = ActorPhysicalAttributesHelper.FormatBirthDate(birthDate, asOf);

        Assert.Equal("05.01.2030", formatted);
    }

    [Fact]
    public void FormatBirthDate_Null_ReturnsNull()
    {
        Assert.Null(ActorPhysicalAttributesHelper.FormatBirthDate(null));
    }

    [Fact]
    public void FormatBirthDate_WithoutAsOf_UsesCurrentUtcDate()
    {
        var birthDate = new DateTime(2000, 1, 1);
        var expectedAge = ActorPhysicalAttributesHelper.CalculateAge(birthDate);

        var formatted = ActorPhysicalAttributesHelper.FormatBirthDate(birthDate);

        Assert.Equal($"01.01.2000 ({expectedAge})", formatted);
    }

    [Theory]
    [InlineData(2008, 9, 28, true)]
    [InlineData(2008, 9, 29, false)]
    [InlineData(1993, 8, 16, true)]
    [InlineData(2030, 1, 1, false)]
    public void IsAllowedBirthDate_RequiresActorToBeAtLeast18(int year, int month, int day, bool expected)
    {
        var asOf = new DateOnly(2026, 9, 28);
        Assert.Equal(expected, ActorPhysicalAttributesHelper.IsAllowedBirthDate(new DateTime(year, month, day), asOf));
    }

    [Fact]
    public void IsAllowedBirthDate_UnknownBirthDate_IsAllowed()
    {
        Assert.True(ActorPhysicalAttributesHelper.IsAllowedBirthDate(null));
    }

    [Fact]
    public void IsAllowedBirthDate_LeapDayBirthday_TurnsEighteenOnMarch1InNonLeapYears()
    {
        var birthDate = new DateTime(2008, 2, 29);
        Assert.False(ActorPhysicalAttributesHelper.IsAllowedBirthDate(birthDate, new DateOnly(2026, 2, 28)));
        Assert.True(ActorPhysicalAttributesHelper.IsAllowedBirthDate(birthDate, new DateOnly(2026, 3, 1)));
    }

    [Theory]
    [InlineData(2026, 9, 28)]
    [InlineData(2028, 2, 29)]
    [InlineData(2027, 3, 1)]
    public void LatestAllowedBirthDate_IsItselfAllowed_AndTheNextDayIsNot(int year, int month, int day)
    {
        var asOf = new DateOnly(year, month, day);
        var latest = ActorPhysicalAttributesHelper.LatestAllowedBirthDate(asOf).ToDateTime(TimeOnly.MinValue);
        Assert.True(ActorPhysicalAttributesHelper.IsAllowedBirthDate(latest, asOf));
        Assert.False(ActorPhysicalAttributesHelper.IsAllowedBirthDate(latest.AddDays(1), asOf));
    }

    [Theory]
    [InlineData("C", true)]
    [InlineData("k", true)]
    [InlineData("DD", false)]
    [InlineData("L", false)]
    [InlineData(null, false)]
    public void IsStandardCupSize_MatchesStandardLettersOnly(string? cupSize, bool expected)
    {
        Assert.Equal(expected, ActorPhysicalAttributesHelper.IsStandardCupSize(cupSize));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("c")]
    public void CupSizeOptions_UnsetOrStandardValue_IsTheStandardList(string? current)
    {
        Assert.Equal(ActorPhysicalAttributesHelper.StandardCupSizes, ActorPhysicalAttributesHelper.CupSizeOptions(current));
    }

    [Fact]
    public void CupSizeOptions_NonStandardValue_IsAppendedNormalized()
    {
        var options = ActorPhysicalAttributesHelper.CupSizeOptions(" dd ");

        Assert.Equal([.. ActorPhysicalAttributesHelper.StandardCupSizes, "DD"], options);
    }

    private static ActorCupSizePeriod Period(int year, int month, int day, string cup) =>
        new() { EffectiveFrom = new DateTime(year, month, day), CupSize = cup };

    [Fact]
    public void CupSizeHistory_NoPeriods_ReturnsSingleOpenEndedBaseStep()
    {
        var step = Assert.Single(ActorPhysicalAttributesHelper.CupSizeHistory("c", []));
        Assert.Equal(new CupSizeStep("C", null, null), step);
    }

    [Fact]
    public void CupSizeHistory_NothingKnown_ReturnsEmpty()
    {
        Assert.Empty(ActorPhysicalAttributesHelper.CupSizeHistory(null, []));
        Assert.Empty(ActorPhysicalAttributesHelper.CupSizeHistory("  ", []));
    }

    [Fact]
    public void CupSizeHistory_OrdersPeriodsAfterBase_WithInclusiveUntilDates()
    {
        var history = ActorPhysicalAttributesHelper.CupSizeHistory("C",
            [Period(2024, 1, 1, "F"), Period(2021, 6, 15, "D")]);

        Assert.Equal(
            [
                new CupSizeStep("C", null, new DateOnly(2021, 6, 14)),
                new CupSizeStep("D", new DateOnly(2021, 6, 15), new DateOnly(2023, 12, 31)),
                new CupSizeStep("F", new DateOnly(2024, 1, 1), null),
            ],
            history);
    }

    [Fact]
    public void CupSizeHistory_NoBaseCup_StartsAtFirstPeriod()
    {
        var history = ActorPhysicalAttributesHelper.CupSizeHistory(null, [Period(2024, 1, 1, "E")]);
        Assert.Equal([new CupSizeStep("E", new DateOnly(2024, 1, 1), null)], history);
    }

    [Fact]
    public void CupSizeHistory_MergesConsecutiveStepsWithTheSameCup()
    {
        var history = ActorPhysicalAttributesHelper.CupSizeHistory("C",
            [Period(2021, 1, 1, "c"), Period(2022, 1, 1, "E"), Period(2023, 1, 1, "E")]);

        Assert.Equal(
            [
                new CupSizeStep("C", null, new DateOnly(2021, 12, 31)),
                new CupSizeStep("E", new DateOnly(2022, 1, 1), null),
            ],
            history);
    }

    [Theory]
    [InlineData(null, 2023, "until 31.12.2023")]
    [InlineData(2021, 2023, "01.01.2021 – 31.12.2023")]
    [InlineData(2024, null, "from 01.01.2024 (current)")]
    [InlineData(null, null, "current")]
    public void FormatCupSizeStepRange_DescribesTheStepsDates(int? fromYear, int? untilYear, string expected)
    {
        var step = new CupSizeStep("C",
            fromYear is { } f ? new DateOnly(f, 1, 1) : null,
            untilYear is { } u ? new DateOnly(u, 12, 31) : null);
        Assert.Equal(expected, ActorPhysicalAttributesHelper.FormatCupSizeStepRange(step));
    }
}
