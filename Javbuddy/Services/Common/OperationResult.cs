namespace Javbuddy.Services.Common;

/// <summary>The outcome of an operation with nothing to return but success or an error message — what each
/// service used to declare its own identical record for.</summary>
public record OperationResult(bool Success, string? ErrorMessage = null)
{
    public static OperationResult Ok() => new(true);
    public static OperationResult Fail(string error) => new(false, error);
}
