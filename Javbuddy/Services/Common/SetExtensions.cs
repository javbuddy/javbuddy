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
}
