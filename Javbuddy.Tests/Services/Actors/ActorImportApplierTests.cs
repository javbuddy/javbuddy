using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Javbuddy.Tests.TestSupport;

namespace Javbuddy.Tests.Services.Actors;

public class ActorImportApplierTests
{
    private static readonly ImportSelection All = new(true, true, true, true, true, true, true);
    private static readonly ImportSelection None = new(false, false, false, false, false, false, false);

    private static ImportedActorData Data(params string[] aliases) => new(
        HeightCm: 160, CupSize: "d", Bust: 90, Waist: 58, Hips: 88, BirthDate: new DateTime(1993, 8, 16),
        IsRetired: true, JapaneseNameKanji: " 三上悠亜 ", JapaneseNameKana: " みかみゆあ ", Aliases: aliases);

    [Fact]
    public void CreateNew_AppliesOnlyTickedFields()
    {
        var all = ActorImportApplier.CreateNew("Yua", "Mikami", Data("Alias"), All);
        Assert.Equal(160, all.HeightCm);
        Assert.Equal("D", all.CupSize);
        Assert.Equal((90, 58, 88), (all.Bust, all.Waist, all.Hips));
        Assert.Equal("三上悠亜", all.JapaneseNameKanji);
        Assert.Equal("みかみゆあ", all.JapaneseNameKana);
        Assert.True(all.IsRetired);
        Assert.Equal(["Alias"], all.Aliases.Select(a => a.Name));

        var none = ActorImportApplier.CreateNew("Yua", "Mikami", Data("Alias"), None);
        Assert.Null(none.HeightCm);
        Assert.Null(none.CupSize);
        Assert.Null(none.BirthDate);
        Assert.Null(none.JapaneseNameKanji);
        Assert.False(none.IsRetired);
        Assert.Empty(none.Aliases);
    }

    [Theory]
    [InlineData("L")]
    [InlineData("DD")]
    public async Task NonStandardCupSize_IsSkipped_LeavingTheRestOfTheImport(string cupSize)
    {
        var data = Data() with { CupSize = cupSize };

        var created = ActorImportApplier.CreateNew("Yua", "Mikami", data, All);
        Assert.Null(created.CupSize);
        Assert.Equal(160, created.HeightCm);

        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();
        var actor = new Actor { FirstName = "Yua", LastName = "Mikami", CupSize = "C" };
        db.Actors.Add(actor);
        await db.SaveChangesAsync();

        ActorImportApplier.ApplyToExisting(db, actor, data, All);

        Assert.Equal("C", actor.CupSize);
        Assert.Equal(160, actor.HeightCm);
    }

    [Fact]
    public async Task ApplyToExisting_KeepsUntickedFields_AndSkipsDuplicateAndDisplayNameAliases()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();
        var actor = new Actor { FirstName = "Yua", LastName = "Mikami", HeightCm = 150 };
        actor.Aliases.Add(new ActorAlias { Name = "Existing" });
        db.Actors.Add(actor);
        await db.SaveChangesAsync();

        ActorImportApplier.ApplyToExisting(db, actor, Data("existing", "Mikami Yua", " New "), None with { Aliases = true, Height = false });

        Assert.Equal(150, actor.HeightCm);
        Assert.Equal(["Existing", "New"], actor.Aliases.Select(a => a.Name));
    }

    [Fact]
    public async Task ApplyToExisting_OverwritesTickedFieldsAndTrimsJapaneseNames()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();
        var actor = new Actor { FirstName = "Yua", HeightCm = 150 };
        db.Actors.Add(actor);
        await db.SaveChangesAsync();

        ActorImportApplier.ApplyToExisting(db, actor, Data(), All);

        Assert.Equal(160, actor.HeightCm);
        Assert.Equal("三上悠亜", actor.JapaneseNameKanji);
        Assert.Equal("みかみゆあ", actor.JapaneseNameKana);
    }
}
