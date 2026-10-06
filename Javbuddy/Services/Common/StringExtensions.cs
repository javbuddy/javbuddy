namespace Javbuddy.Services.Common;

public static class StringExtensions
{
    public static string? TrimToNull(this string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
