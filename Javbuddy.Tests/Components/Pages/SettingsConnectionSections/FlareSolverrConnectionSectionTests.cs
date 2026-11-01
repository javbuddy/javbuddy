using Bunit;
using Javbuddy.Components.Pages.SettingsConnectionSections;
using Javbuddy.Data;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.Settings;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages.SettingsConnectionSections;

public class FlareSolverrConnectionSectionTests : BunitContext
{
    private readonly IChallengeSolverClient solver = Substitute.For<IChallengeSolverClient>();
    private readonly TestDbContextFactory dbFactory = new();

    public FlareSolverrConnectionSectionTests()
    {
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(dbFactory);
        Services.AddSingleton(solver);
        Services.AddSingleton<IConnectionSettingsSaveService>(new ConnectionSettingsSaveService(dbFactory));
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) dbFactory.Dispose();
        base.Dispose(disposing);
    }

    [Fact]
    public void RendersTitleAndDefaultTimeout()
    {
        var cut = Render<FlareSolverrConnectionSection>();

        Assert.Contains("Challenge solver", cut.Find(".card-title").TextContent);
        Assert.Equal("60000", cut.FindAll("input")[1].GetAttribute("value"));
    }

    [Fact]
    public async Task Save_PersistsTheAddress()
    {
        var cut = Render<FlareSolverrConnectionSection>();

        cut.FindAll("input")[0].Change("http://flaresolverr:8191");
        cut.Find("form").Submit();

        await using var db = await dbFactory.CreateDbContextAsync();
        Assert.Equal("http://flaresolverr:8191", (await db.FlareSolverrSettings.SingleAsync()).BaseUrl);
        Assert.Contains("Saved.", cut.Markup);
    }

    [Fact]
    public void TestConnection_ShowsTheSolverVersion()
    {
        solver.TestConnectionAsync(Arg.Any<CancellationToken>()).Returns(new SolverTestResult(true, "Connected to the challenge solver.", "2.0.0"));
        var cut = Render<FlareSolverrConnectionSection>();
        cut.FindAll("input")[0].Change("http://trawl:8191");

        cut.Find("button[type=button]").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Connected to the challenge solver.", cut.Markup);
            Assert.Contains("2.0.0", cut.Markup);
        });
    }

    [Fact]
    public void EnvironmentConfiguration_LocksTheForm()
    {
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["FlareSolverr:BaseUrl"] = "http://env:8191" }).Build());

        var cut = Render<FlareSolverrConnectionSection>();

        Assert.Contains("FlareSolverr__BaseUrl", cut.Markup);
        Assert.Equal("http://env:8191", cut.FindAll("input")[0].GetAttribute("value"));
        Assert.True(cut.FindAll("input")[0].HasAttribute("disabled"));
    }
}
