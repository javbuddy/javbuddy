namespace Javbuddy.Services.Infrastructure;

/// <summary>Compares a timestamp stored in the database with a file's on-disk time at microsecond precision.
/// A file system reports 100 ns ticks, which SQLite stores as-is but Postgres (microsecond timestamps) truncates, so an
/// exact comparison of the stored value with a fresh reading would call an unchanged file changed on Postgres.</summary>
public static class FileTimestamps
{
    private const long TicksPerMicrosecond = 10;

    /// <summary>True when both are set and fall in the same microsecond, or when both are null.</summary>
    public static bool Same(DateTime? stored, DateTime? onDisk) =>
        stored is { } left && onDisk is { } right
            ? left.Ticks / TicksPerMicrosecond == right.Ticks / TicksPerMicrosecond
            : stored is null && onDisk is null;

    /// <summary>True when the stored time is the on-disk time or later, at microsecond precision.</summary>
    public static bool AtLeast(DateTime stored, DateTime onDisk) =>
        stored.Ticks / TicksPerMicrosecond >= onDisk.Ticks / TicksPerMicrosecond;
}
