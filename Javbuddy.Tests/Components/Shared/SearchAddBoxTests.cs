using Bunit;
using Javbuddy.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Javbuddy.Tests.Components.Shared;

public class SearchAddBoxTests : BunitContext
{
    private IRenderedComponent<SearchAddBox<string>> RenderBox(
        Func<string, CancellationToken, Task<IReadOnlyList<string>>> search,
        Action<string>? onAdd = null,
        Action<string>? onFreeText = null) =>
        Render<SearchAddBox<string>>(p =>
        {
            p.Add(x => x.ClassPrefix, "thing-search")
                .Add(x => x.Placeholder, "Find…")
                .Add(x => x.Search, search)
                .Add(x => x.ItemTemplate, item => b => b.AddContent(0, $"<{item}>"))
                .Add(x => x.OnAdd, EventCallback.Factory.Create<string>(this, s => onAdd?.Invoke(s)));
            if (onFreeText is not null) p.Add(x => x.OnAddFreeText, EventCallback.Factory.Create<string>(this, onFreeText));
        });

    private static Task<IReadOnlyList<string>> Results(params string[] items) => Task.FromResult<IReadOnlyList<string>>(items);

    [Fact]
    public void Initially_ShowsThePrefixedInputWithItsPlaceholder_AndNoResults()
    {
        var cut = RenderBox((_, _) => Results());

        Assert.Equal("Find…", cut.Find("input.thing-search-input").GetAttribute("placeholder"));
        Assert.Contains("thing-search", cut.Find(".search-add").ClassList);
        Assert.Empty(cut.FindAll(".thing-search-candidate"));
    }

    [Fact]
    public void Typing_ListsTheSearchResultsThroughTheItemTemplate()
    {
        var cut = RenderBox((_, _) => Results("a", "b"));

        cut.Find("input").Input("x");
        cut.WaitForElements(".thing-search-candidate");

        Assert.Equal(["<a>", "<b>"], cut.FindAll(".thing-search-results .thing-search-candidate").Select(c => c.TextContent.Trim()));
    }

    [Fact]
    public void TypingQuickly_SearchesOnceForTheFinalText()
    {
        var terms = new List<string>();
        var cut = RenderBox((term, _) => { terms.Add(term); return Results("a"); });
        var input = cut.Find("input");

        input.Input("s");
        input.Input("so");
        input.Input("sol");
        cut.WaitForElements(".thing-search-candidate");

        Assert.Equal(["sol"], terms);
    }

    [Fact]
    public void BlankInput_ClearsTheResultsWithoutSearching()
    {
        var searches = 0;
        var cut = RenderBox((_, _) => { searches++; return Results("a"); });
        cut.Find("input").Input("x");
        cut.WaitForElements(".thing-search-candidate");

        cut.Find("input").Input("  ");

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".thing-search-candidate")));
        Assert.Equal(1, searches);
    }

    [Fact]
    public void ClickingAResult_RaisesOnAddWithIt_AndResetsTheBox()
    {
        var added = new List<string>();
        var cut = RenderBox((_, _) => Results("a", "b"), added.Add);
        cut.Find("input").Input("x");
        cut.WaitForElements(".thing-search-candidate");

        cut.FindAll(".thing-search-candidate")[1].Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(["b"], added);
            Assert.Empty(cut.FindAll(".thing-search-candidate"));
            Assert.Equal("", cut.Find("input").GetAttribute("value") ?? "");
        });
    }

    [Fact]
    public void Enter_AddsTheTrimmedTextAsIs_OnlyWhenOnAddFreeTextIsSet()
    {
        var typed = new List<string>();
        var cut = RenderBox((_, _) => Results(), onFreeText: typed.Add);
        cut.Find("input").Input("  Brand New ");

        cut.Find("input").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        // A pending debounced search resumes on the thread pool, so an event can be dispatched after it returns.
        cut.WaitForAssertion(() =>
        {
            Assert.Equal(["Brand New"], typed);
            Assert.Equal("", cut.Find("input").GetAttribute("value") ?? "");
        });

        var plain = RenderBox((_, _) => Results());
        plain.Find("input").Input("Brand New");
        plain.Find("input").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        plain.WaitForAssertion(() => Assert.Equal("Brand New", plain.Find("input").GetAttribute("value")));
    }
}
