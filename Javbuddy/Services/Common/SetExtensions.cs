namespace Javbuddy.Services.Common;

public static class SetExtensions
{
    /// <summary>Removes <paramref name="value"/> if the set has it, otherwise adds it — a checkbox/chip toggle.</summary>
    public static void Toggle<T>(this ISet<T> set, T value)
    {
        if (!set.Remove(value))
        {
            set.Add(value);
        }
    }

    /// <summary>Steps <paramref name="value"/> through an include/exclude filter's three states: neither → included →
    /// excluded → neither. It is in at most one of the two sets.</summary>
    public static void CycleInclusion<T>(this ISet<T> included, ISet<T> excluded, T value)
    {
        if (included.Remove(value))
        {
            excluded.Add(value);
        }
        else if (!excluded.Remove(value))
        {
            included.Add(value);
        }
    }
}
