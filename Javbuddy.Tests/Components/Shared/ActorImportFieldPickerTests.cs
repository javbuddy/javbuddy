using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Javbuddy.Services.MinnanoAv;

namespace Javbuddy.Tests.Components.Shared;

public class ActorImportFieldPickerTests : BunitContext
{
    private static readonly Actor TheActor = new() { Id = 1, FirstName = "Yua", LastName = "Mikami", JapaneseNameKanji = "旧三上", HeightCm = 160 };

    private static ImportedActorData Data(
        string? kanji = "三上悠亜",
        string? kana = null,
        int? height = 159,
        IReadOnlyList<string>? aliases = null) =>
        new(height, "E", 90, 58, 85, new DateTime(1993, 8, 16), false, kanji, kana, aliases ?? ["Alias One"]);

    private IRenderedComponent<ActorImportFieldPicker> Render(
        ImportedActorData data,
        IActorImportOptions options,
        bool hasKnownAttributes = true,
        Action<int>? onChanged = null) =>
        Render<ActorImportFieldPicker>(p => p
            .Add(x => x.Actor, TheActor)
            .Add(x => x.Values, data)
            .Add(x => x.Options, options)
            .Add(x => x.SourceName, "minnano-av.com")
            .Add(x => x.HasKnownAttributes, hasKnownAttributes)
            .Add(x => x.OnOptionsChanged, () => onChanged?.Invoke(0)));

    [Fact]
    public void ShowsNewAndCurrentValuesPerField()
    {
        var cut = Render(Data(kana: "みかみゆあ"), new MinnanoAvImportOptions());

        Assert.Contains("三上悠亜 (みかみゆあ)", cut.Find("#field-kanji").Closest(".import-field-item")!.TextContent);
        var height = cut.Find("#field-height").Closest(".import-field-item")!.TextContent;
        Assert.Contains("159 cm", height);
        Assert.Contains("Current: 160 cm", height);
        Assert.Contains("90-58-85", cut.Find("#field-measurements").Closest(".import-field-item")!.TextContent);
        Assert.Contains("1993-08-16", cut.Find("#field-birthdate").Closest(".import-field-item")!.TextContent);
        Assert.Contains("Alias One", cut.Find("#field-aliases").Closest(".import-field-item")!.TextContent);
    }

    [Fact]
    public void UnavailableFieldIsDisabledAndMarked()
    {
        var cut = Render(Data(height: null), new MinnanoAvImportOptions());

        var row = cut.Find("#field-height").Closest(".import-field-item")!;
        Assert.Contains("field-unavailable", row.ClassName);
        Assert.True(cut.Find("#field-height").HasAttribute("disabled"));
        Assert.Contains("—", row.TextContent);
    }

    [Fact]
    public void TogglingACheckbox_UpdatesOptionsAndNotifiesHost()
    {
        var options = new MinnanoAvImportOptions();
        var notified = 0;
        var cut = Render(Data(), options, onChanged: _ => notified++);

        cut.Find("#field-height").Change(false);

        Assert.False(options.ImportHeight);
        Assert.True(options.ImportCupSize);
        Assert.Equal(1, notified);
    }

    [Fact]
    public void DeselectAllThenSelectAll_TicksOnlyAvailableFields()
    {
        var options = new MinnanoAvImportOptions();
        var cut = Render(Data(height: null, aliases: []), options);

        cut.FindAll("button.field-toggle-btn").First(b => b.TextContent.Contains("Deselect all")).Click();
        Assert.False(options.ImportJapaneseName);
        Assert.False(options.ImportCupSize);

        cut.FindAll("button.field-toggle-btn").First(b => b.TextContent.Contains("Select all")).Click();
        Assert.True(options.ImportJapaneseName);
        Assert.True(options.ImportCupSize);
        Assert.True(options.ImportMeasurements);
        Assert.False(options.ImportHeight);
        Assert.False(options.ImportAliases);
    }

    [Fact]
    public void NoKnownAttributes_ShowsSourceWarning()
    {
        var cut = Render(Data(), new MinnanoAvImportOptions(), hasKnownAttributes: false);

        Assert.Contains("unknown for this performer on minnano-av.com", cut.Find(".alert-warning").TextContent);
    }

    [Fact]
    public void SelectAllAndDeselectAll_RaiseHostCallbacksForExtraRows()
    {
        var selected = 0;
        var deselected = 0;
        var cut = Render<ActorImportFieldPicker>(p => p
            .Add(x => x.Actor, TheActor)
            .Add(x => x.Values, Data())
            .Add(x => x.Options, new MinnanoAvImportOptions())
            .Add(x => x.SourceName, "x")
            .Add(x => x.OnSelectAll, () => selected++)
            .Add(x => x.OnDeselectAll, () => deselected++)
            .Add(x => x.ExtraRows, "<div class=\"import-field-item\" id=\"extra\">extra</div>"));

        cut.FindAll("button.field-toggle-btn").First(b => b.TextContent.Contains("Select all")).Click();
        cut.FindAll("button.field-toggle-btn").First(b => b.TextContent.Contains("Deselect all")).Click();

        Assert.Equal(1, selected);
        Assert.Equal(1, deselected);
        Assert.NotNull(cut.Find("#extra"));
    }
}
