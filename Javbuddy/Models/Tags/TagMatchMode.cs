namespace Javbuddy.Models;

/// <summary>How a TagReplacementRule.SourceValue or IgnoredTag.Value compares against a raw
/// scraped/imported tag string. There is deliberately no regex mode.</summary>
public enum TagMatchMode
{
    Exact,
    CaseInsensitive
}
