using Bunit;
using Javbuddy.Components.Pages.SettingsConnectionSections;
using Javbuddy.Data;
using Javbuddy.Services.Javinizer;
using Javbuddy.Services.Settings;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages.SettingsConnectionSections;

public class JavinizerConnectionSectionConnectionTests : BunitContext
{
    private IJavinizerClient SetUpServices(IConfiguration? configuration = null)
    {
        var dbFactory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(dbFactory);
        Services.AddSingleton<IConnectionSettingsSaveService>(new ConnectionSettingsSaveService(dbFactory));
        Services.AddSingleton(configuration ?? new ConfigurationBuilder().Build());

        var javinizerClient = Substitute.For<IJavinizerClient>();
        Services.AddSingleton(javinizerClient);
        return javinizerClient;
    }

    [Fact]
    public void WhenConnectionSucceeds_RendersVersion()
    {
        var javinizerClient = SetUpServices();
        javinizerClient.TestConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(new JavinizerTestResult(true, "Connected successfully.", "1.5.1"));

        var cut = Render<JavinizerConnectionSection>();
        var testButton = cut.FindAll("button").First(b => b.TextContent.Trim() == "Test connection");
        testButton.Click();

        cut.WaitForAssertion(() =>
        {
            var alert = cut.Find(".alert-success");
            Assert.Contains("Connected successfully.", alert.TextContent);
            Assert.Contains("Server version:", alert.TextContent);
            Assert.Contains("1.5.1", alert.TextContent);
        });
    }

    [Fact]
    public void WhenConnectionSucceeds_WithoutVersion_OnlyRendersSuccessMessage()
    {
        var javinizerClient = SetUpServices();
        javinizerClient.TestConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(new JavinizerTestResult(true, "Connected successfully."));

        var cut = Render<JavinizerConnectionSection>();
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
        var javinizerClient = SetUpServices();
        javinizerClient.TestConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(new JavinizerTestResult(false, "Reached the server, but the API token was rejected."));

        var cut = Render<JavinizerConnectionSection>();
        var testButton = cut.FindAll("button").First(b => b.TextContent.Trim() == "Test connection");
        testButton.Click();

        cut.WaitForAssertion(() =>
        {
            var alert = cut.Find(".alert-danger");
            Assert.Contains("Reached the server, but the API token was rejected.", alert.TextContent);
            Assert.DoesNotContain("Server version:", cut.Markup);
        });
    }
}
