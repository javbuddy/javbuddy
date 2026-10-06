using Bunit;
using Javbuddy.Components.Pages.SettingsConnectionSections;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Services.Settings;
using Javbuddy.Services.Trickplay;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages.SettingsConnectionSections;

public class JellyfinConnectionSectionConnectionTests : BunitContext
{
    private IJellyfinClient SetUpServices(IConfiguration? configuration = null)
    {
        var dbFactory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(dbFactory);
        Services.AddSingleton<IConnectionSettingsSaveService>(new ConnectionSettingsSaveService(dbFactory));
        var trickplaySettings = Substitute.For<ITrickplaySettingsService>();
        trickplaySettings.GetEffectiveAsync(Arg.Any<CancellationToken>()).Returns(new TrickplaySettings());
        Services.AddSingleton(trickplaySettings);
        Services.AddSingleton(configuration ?? new ConfigurationBuilder().Build());

        var jellyfinClient = Substitute.For<IJellyfinClient>();
        Services.AddSingleton(jellyfinClient);
        return jellyfinClient;
    }

    [Fact]
    public void WhenConnectionSucceeds_RendersVersionAndServerId()
    {
        var jellyfinClient = SetUpServices();
        jellyfinClient.TestConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(new JellyfinTestResult(true, "Connected successfully.", "10.9.7", "server-abc-123"));

        var cut = Render<JellyfinConnectionSection>();
        var testButton = cut.FindAll("button").First(b => b.TextContent.Trim() == "Test connection");
        testButton.Click();

        cut.WaitForAssertion(() =>
        {
            var alert = cut.Find(".alert-success");
            Assert.Contains("Connected successfully.", alert.TextContent);
            Assert.Contains("Server version:", alert.TextContent);
            Assert.Contains("10.9.7", alert.TextContent);
            Assert.Contains("Server ID:", alert.TextContent);
            Assert.Contains("server-abc-123", alert.TextContent);
        });
    }

    [Fact]
    public void WhenConnectionSucceeds_WithoutVersionOrServerId_OnlyRendersSuccessMessage()
    {
        var jellyfinClient = SetUpServices();
        jellyfinClient.TestConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(new JellyfinTestResult(true, "Connected successfully."));

        var cut = Render<JellyfinConnectionSection>();
        var testButton = cut.FindAll("button").First(b => b.TextContent.Trim() == "Test connection");
        testButton.Click();

        cut.WaitForAssertion(() =>
        {
            var alert = cut.Find(".alert-success");
            Assert.Contains("Connected successfully.", alert.TextContent);
            Assert.DoesNotContain("Server version:", alert.TextContent);
            Assert.DoesNotContain("Server ID:", alert.TextContent);
        });
    }

    [Fact]
    public void WhenConnectionFails_RendersDangerAlert_WithoutDetails()
    {
        var jellyfinClient = SetUpServices();
        jellyfinClient.TestConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(new JellyfinTestResult(false, "Reached the server, but the API key was rejected."));

        var cut = Render<JellyfinConnectionSection>();
        var testButton = cut.FindAll("button").First(b => b.TextContent.Trim() == "Test connection");
        testButton.Click();

        cut.WaitForAssertion(() =>
        {
            var alert = cut.Find(".alert-danger");
            Assert.Contains("Reached the server, but the API key was rejected.", alert.TextContent);
            Assert.DoesNotContain("Server version:", cut.Markup);
            Assert.DoesNotContain("Server ID:", cut.Markup);
        });
    }
}
