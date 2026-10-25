using Javbuddy.Services.Infrastructure;

namespace Javbuddy.Tests.TestSupport;

internal static class FileTimestampAssert
{
    /// <summary>Asserts a timestamp read back from the database equals a file's on-disk time. Postgres keeps microseconds
    /// where the file system reports 100 ns ticks, so the comparison is at microsecond precision (see <see cref="FileTimestamps"/>).</summary>
    public static void Same(DateTime expected, DateTime? actual) =>
        Assert.True(FileTimestamps.Same(expected, actual), $"Expected {expected:O} but was {actual:O} (compared at microsecond precision).");
}
