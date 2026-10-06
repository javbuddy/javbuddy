using Bunit;
using Javbuddy.Components.Shared;
using Microsoft.AspNetCore.Components;

namespace Javbuddy.Tests.Components.Shared;

public class StatusTileTests : BunitContext
{
    [Fact]
    public void Configured_ShowsConfiguredBadgeAndOkDot()
    {
        var cut = Render<StatusTile>(p =>
        {
            p.Add(x => x.Title, "javinizer-go");
            p.Add(x => x.Description, "Fetches metadata.");
            p.Add(x => x.Configured, true);
            p.Add(x => x.Summary, "http://localhost:8765");
            p.Add(x => x.OnOpen, EventCallback.Empty);
        });

        Assert.Contains("Configured", cut.Find(".status-tile-badge").TextContent);
        Assert.NotEmpty(cut.FindAll(".status-tile-dot.status-tile-ok"));
        Assert.Contains("http://localhost:8765", cut.Find(".status-tile-meta").TextContent);
    }

    [Fact]
    public void NotConfigured_ShowsNotConfiguredBadge()
    {
        var cut = Render<StatusTile>(p =>
        {
            p.Add(x => x.Title, "Prowlarr");
            p.Add(x => x.Description, "Searches indexers.");
            p.Add(x => x.Configured, false);
            p.Add(x => x.OnOpen, EventCallback.Empty);
        });

        Assert.Contains("Not configured", cut.Find(".status-tile-badge").TextContent);
        Assert.NotEmpty(cut.FindAll(".status-tile-dot.status-tile-off"));
    }

    [Fact]
    public void CustomLabels_OverrideDefaultBadgeText()
    {
        var cut = Render<StatusTile>(p =>
        {
            p.Add(x => x.Title, "MediaInfo");
            p.Add(x => x.Description, "Probes video files.");
            p.Add(x => x.Configured, true);
            p.Add(x => x.ConfiguredLabel, "Enabled");
            p.Add(x => x.NotConfiguredLabel, "Disabled");
            p.Add(x => x.OnOpen, EventCallback.Empty);
        });

        Assert.Contains("Enabled", cut.Find(".status-tile-badge").TextContent);
    }

    [Fact]
    public void NoSummary_OmitsMetaLine()
    {
        var cut = Render<StatusTile>(p =>
        {
            p.Add(x => x.Title, "MediaInfo");
            p.Add(x => x.Description, "Probes video files.");
            p.Add(x => x.Configured, true);
            p.Add(x => x.OnOpen, EventCallback.Empty);
        });

        Assert.Empty(cut.FindAll(".status-tile-meta"));
    }

    [Fact]
    public void EnvConfigured_ShowsEnvChip()
    {
        var cut = Render<StatusTile>(p =>
        {
            p.Add(x => x.Title, "qBittorrent");
            p.Add(x => x.Description, "Download client.");
            p.Add(x => x.Configured, true);
            p.Add(x => x.EnvConfigured, true);
            p.Add(x => x.OnOpen, EventCallback.Empty);
        });

        Assert.Contains("ENV configured", cut.Markup);
    }

    [Fact]
    public void NotEnvConfigured_DoesNotShowEnvChip()
    {
        var cut = Render<StatusTile>(p =>
        {
            p.Add(x => x.Title, "qBittorrent");
            p.Add(x => x.Description, "Download client.");
            p.Add(x => x.Configured, true);
            p.Add(x => x.EnvConfigured, false);
            p.Add(x => x.OnOpen, EventCallback.Empty);
        });

        Assert.DoesNotContain("ENV configured", cut.Markup);
    }

    [Fact]
    public void Click_InvokesOnOpen()
    {
        var opened = false;

        var cut = Render<StatusTile>(p =>
        {
            p.Add(x => x.Title, "javinizer-go");
            p.Add(x => x.Description, "Fetches metadata.");
            p.Add(x => x.Configured, true);
            p.Add(x => x.OnOpen, EventCallback.Factory.Create(this, () => opened = true));
        });

        cut.Find(".status-tile").Click();

        Assert.True(opened);
    }
}
