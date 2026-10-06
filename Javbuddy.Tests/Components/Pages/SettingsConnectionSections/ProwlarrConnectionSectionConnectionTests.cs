using Bunit;
using Javbuddy.Components.Pages.SettingsConnectionSections;
using Javbuddy.Data;
using Javbuddy.Services.Prowlarr;
using Javbuddy.Services.Settings;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages.SettingsConnectionSections;

public class ProwlarrConnectionSectionConnectionTests : BunitContext
{
    private IProwlarrClient SetUpServices(IConfiguration? configuration = null)
    {
        var dbFactory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(dbFactory);
        Services.AddSingleton<IConnectionSettingsSaveService>(new ConnectionSettingsSaveService(dbFactory));
        Services.AddSingleton(configuration ?? new ConfigurationBuilder().Build());

        var prowlarrClient = Substitute.For<IProwlarrClient>();
        Services.AddSingleton(prowlarrClient);
        return prowlarrClient;
    }

    [Fact]
    public void WhenConnectionSucceeds_RendersVersion()
    {
        var prowlarrClient = SetUpServices();
        prowlarrClient.TestConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(new ProwlarrTestResult(true, "Connected successfully.", "1.24.3.4754"));

        var cut = Render<ProwlarrConnectionSection>();
        var testButton = cut.FindAll("button").First(b => b.TextContent.Trim() == "Test connection");
        testButton.Click();

        cut.WaitForAssertion(() =>
        {
            var alert = cut.Find(".alert-success");
            Assert.Contains("Connected successfully.", alert.TextContent);
            Assert.Contains("Server version:", alert.TextContent);
            Assert.Contains("1.24.3.4754", alert.TextContent);
        });
    }

    [Fact]
    public void WhenConnectionSucceeds_WithoutVersion_OnlyRendersSuccessMessage()
    {
        var prowlarrClient = SetUpServices();
        prowlarrClient.TestConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(new ProwlarrTestResult(true, "Connected successfully."));

        var cut = Render<ProwlarrConnectionSection>();
        var testButton = cut.FindAll("button").First(b => b.TextContent.Trim() == "Test connection");
        testButton.Click();

        cut.WaitForAssertion(() =>
        {
            var alert = cut.Find(".alert-success");
            Assert.Contains("Connected successfully.", alert.TextContent);
            Assert.DoesNotContain("Server version:", alert.TextContent);
        });
    }

    [Fact]
    public void WhenConnectionFails_RendersDangerAlert_WithoutDetails()
    {
        var prowlarrClient = SetUpServices();
        prowlarrClient.TestConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(new ProwlarrTestResult(false, "Reached the server, but the API key was rejected."));

        var cut = Render<ProwlarrConnectionSection>();
        var testButton = cut.FindAll("button").First(b => b.TextContent.Trim() == "Test connection");
        testButton.Click();

        cut.WaitForAssertion(() =>
        {
            var alert = cut.Find(".alert-danger");
            Assert.Contains("Reached the server, but the API key was rejected.", alert.TextContent);
            Assert.DoesNotContain("Server version:", cut.Markup);
        });
    }
}
