using Javbuddy.Services.Common;

namespace Javbuddy.Tests.Services.Common;

public class SelectionSetTests
{
    [Fact]
    public void Set_ChecksAndUnchecksOneRow()
    {
        var set = new SelectionSet<int>();

        set.Set(1, true);
        set.Set(1, true);
        Assert.Equal(1, set.Count);
        Assert.True(set.Contains(1));

        set.Set(1, false);
        Assert.Equal(0, set.Count);
    }

    [Fact]
    public void Toggle_FlipsTheRow()
    {
        var set = new SelectionSet<string>();

        set.Toggle("a");
        Assert.True(set.Contains("a"));

        set.Toggle("a");
        Assert.False(set.Contains("a"));
    }

    [Fact]
    public void SetAll_OnlyTouchesTheGivenRows()
    {
        var set = new SelectionSet<int>();
        set.Set(9, true);

        set.SetAll([1, 2, 3], true);
        Assert.Equal([1, 2, 3, 9], set.ToList().Order());

        set.SetAll([1, 2, 3], false);
        Assert.Equal([9], set.ToList());
    }

    [Fact]
    public void AllSelected_IsFalseForNoRows_AndTrueOnlyWhenEveryRowIsChecked()
    {
        var set = new SelectionSet<int>();
        Assert.False(set.AllSelected([]));

        set.SetAll([1, 2], true);
        Assert.True(set.AllSelected([1, 2]));
        Assert.False(set.AllSelected([1, 2, 3]));
    }

    [Fact]
    public void RetainOnly_DropsRowsNoLongerListed()
    {
        var set = new SelectionSet<int>();
        set.SetAll([1, 2, 3], true);

        set.RetainOnly([2, 3, 4]);

        Assert.Equal([2, 3], set.ToList().Order());
    }

    [Fact]
    public void Clear_EmptiesTheSet()
    {
        var set = new SelectionSet<int>();
        set.SetAll([1, 2], true);

        set.Clear();

        Assert.Equal(0, set.Count);
    }
}
