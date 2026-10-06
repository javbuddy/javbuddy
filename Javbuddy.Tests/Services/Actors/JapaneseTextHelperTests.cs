using Javbuddy.Services.Actors;

namespace Javbuddy.Tests.Services.Actors;

public class JapaneseTextHelperTests
{
    [Theory]
    [InlineData("波多野結衣", true)]
    [InlineData("新木 希空", true)]
    [InlineData("はたのゆい", true)]
    [InlineData("アオイ", true)]
    [InlineData("Hatano Yui", false)]
    [InlineData("123", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void HasJapaneseCharacters_DetectsJapaneseScripts(string? text, bool expected)
    {
        Assert.Equal(expected, JapaneseTextHelper.HasJapaneseCharacters(text));
    }

    [Theory]
    [InlineData("波多野結衣", true)]
    [InlineData("新木希空", true)]
    [InlineData("はたのゆい", false)]
    [InlineData("アオイ", false)]
    [InlineData("Hatano Yui", false)]
    [InlineData(null, false)]
    public void HasKanji_DetectsKanjiOnly(string? text, bool expected)
    {
        Assert.Equal(expected, JapaneseTextHelper.HasKanji(text));
    }

    [Theory]
    [InlineData("はたのゆい", true)]
    [InlineData("アオイ", true)]
    [InlineData("波多野結衣", false)]
    [InlineData("桐嶋りの", false)] // Has Kanji + Kana, so not pure kana
    [InlineData("Hatano Yui", false)]
    [InlineData(null, false)]
    public void IsPureKana_DetectsKanaWithoutKanji(string? text, bool expected)
    {
        Assert.Equal(expected, JapaneseTextHelper.IsPureKana(text));
    }
}
