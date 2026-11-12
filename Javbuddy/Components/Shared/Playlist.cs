namespace Javbuddy.Components.Shared;

/// <summary>A fixed list the player steps through: Play all / Shuffle on the Movies page and the Scenes
/// wall, and Movie Detail's Play menu. Holds the order and the current position; the host decides what
/// playing an entry means.</summary>
public sealed class Playlist<T>(T[] items, int startIndex = 0)
{
    /// <summary>The current entry; the start index is clamped into the list.</summary>
    public int Index { get; private set; } = Math.Clamp(startIndex, 0, Math.Max(items.Length - 1, 0));

    public int Count => items.Length;

    /// <summary>1-based, for "3 of 12".</summary>
    public int Position => Index + 1;

    public T Current => items[Index];

    public bool CanGoPrevious => Index > 0;

    /// <summary>Moves to the next entry; false (and stays) after the last.</summary>
    public bool TryNext()
    {
        if (Index + 1 >= items.Length) return false;
        Index++;
        return true;
    }

    /// <summary>Moves back one entry; false (and stays) at the first.</summary>
    public bool TryPrevious()
    {
        if (Index == 0) return false;
        Index--;
        return true;
    }
}
