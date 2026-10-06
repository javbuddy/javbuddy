using Bunit;
using Javbuddy.Components.Shared;
using Microsoft.AspNetCore.Components;

namespace Javbuddy.Tests.Components.Shared;

public class ConnectionSettingsCardTests : BunitContext
{
    private sealed class TestSettings
    {
        public string? Value { get; set; }
    }

    private static void AddDefaults(ComponentParameterCollectionBuilder<ConnectionSettingsCard<TestSettings>> b)
    {
        b.Add(x => x.Title, "Example");
        b.Add(x => x.Settings, new TestSettings());
        b.Add(x => x.FormName, "example-settings");
        b.Add(x => x.OnSave, EventCallback.Empty);
    }

    [Fact]
    public void SettingsNull_ShowsLoadingIndicatorInsteadOfForm()
    {
        var cut = Render<ConnectionSettingsCard<TestSettings>>(p =>
        {
            p.Add(x => x.Title, "Example");
            p.Add(x => x.Settings, null);
            p.Add(x => x.FormName, "example-settings");
            p.Add(x => x.OnSave, EventCallback.Empty);
        });

        Assert.NotEmpty(cut.FindAll("em"));
        Assert.Empty(cut.FindAll("form"));
    }

    [Fact]
    public void SettingsPresent_RendersFormWithSaveButton()
    {
        var cut = Render<ConnectionSettingsCard<TestSettings>>(AddDefaults);

        Assert.NotEmpty(cut.FindAll("form"));
        var saveButton = cut.Find("button[type=submit]");
        Assert.Equal("Save", saveButton.TextContent.Trim());
    }

    [Fact]
    public void EnvConfiguredTrue_ShowsEnvBannerAndDisablesSave()
    {
        var cut = Render<ConnectionSettingsCard<TestSettings>>(p =>
        {
            AddDefaults(p);
            p.Add(x => x.EnvConfigured, true);
            p.Add(x => x.EnvBanner, "env-configured banner text");
        });

        Assert.Contains("env-configured banner text", cut.Find(".alert-info").TextContent);
        Assert.True(cut.Find("button[type=submit]").HasAttribute("disabled"));
    }

    [Fact]
    public void EnvConfiguredFalse_NoBanner_SaveEnabled()
    {
        var cut = Render<ConnectionSettingsCard<TestSettings>>(AddDefaults);

        Assert.Empty(cut.FindAll(".alert-info"));
        Assert.False(cut.Find("button[type=submit]").HasAttribute("disabled"));
    }

    [Fact]
    public void ShowTestButtonFalse_NoTestButtonRendered()
    {
        var cut = Render<ConnectionSettingsCard<TestSettings>>(p =>
        {
            AddDefaults(p);
            p.Add(x => x.ShowTestButton, false);
        });

        var buttons = cut.FindAll("button");
        Assert.DoesNotContain(buttons, b => b.TextContent.Contains("Test connection"));
    }

    [Fact]
    public void ShowTestButtonTrue_RendersTestButtonWithTestingLabel()
    {
        var cut = Render<ConnectionSettingsCard<TestSettings>>(p =>
        {
            AddDefaults(p);
            p.Add(x => x.OnTest, EventCallback.Empty);
            p.Add(x => x.Testing, true);
        });

        var buttons = cut.FindAll("button");
        Assert.Contains(buttons, b => b.TextContent.Contains("Testing…"));
    }

    [Fact]
    public void TestResultSuccess_RendersSuccessAlert()
    {
        var cut = Render<ConnectionSettingsCard<TestSettings>>(p =>
        {
            AddDefaults(p);
            p.Add(x => x.TestResult, (true, "Connected successfully."));
        });

        var alert = cut.Find(".alert-success");
        Assert.Contains("Connected successfully.", alert.TextContent);
    }

    [Fact]
    public void TestResultFailure_RendersDangerAlert()
    {
        var cut = Render<ConnectionSettingsCard<TestSettings>>(p =>
        {
            AddDefaults(p);
            p.Add(x => x.TestResult, (false, "Could not reach server."));
        });

        var alert = cut.Find(".alert-danger");
        Assert.Contains("Could not reach server.", alert.TextContent);
    }

    [Fact]
    public void TestResultSuccess_RendersTestResultDetails_WhenProvided()
    {
        var cut = Render<ConnectionSettingsCard<TestSettings>>(p =>
        {
            AddDefaults(p);
            p.Add(x => x.TestResult, (true, "Connected successfully."));
            p.Add(x => x.TestResultDetails, builder => builder.AddMarkupContent(0, "<div class=\"custom-details\">Version 10.9</div>"));
        });

        var alert = cut.Find(".alert-success");
        Assert.Contains("Connected successfully.", alert.TextContent);
        Assert.Contains("Version 10.9", cut.Find(".custom-details").TextContent);
    }

    [Fact]
    public void TestResultFailure_DoesNotRenderTestResultDetails()
    {
        var cut = Render<ConnectionSettingsCard<TestSettings>>(p =>
        {
            AddDefaults(p);
            p.Add(x => x.TestResult, (false, "Could not reach server."));
            p.Add(x => x.TestResultDetails, builder => builder.AddMarkupContent(0, "<div class=\"custom-details\">Version 10.9</div>"));
        });

        var alert = cut.Find(".alert-danger");
        Assert.Contains("Could not reach server.", alert.TextContent);
        Assert.Empty(cut.FindAll(".custom-details"));
    }

    [Fact]
    public void SavedTrue_RendersSavedAlert()
    {
        var cut = Render<ConnectionSettingsCard<TestSettings>>(p =>
        {
            AddDefaults(p);
            p.Add(x => x.Saved, true);
        });

        Assert.Contains(cut.FindAll(".alert-success"), a => a.TextContent.Trim() == "Saved.");
    }

    [Fact]
    public void IsFirstTrue_OmitsTopMarginClass()
    {
        var cut = Render<ConnectionSettingsCard<TestSettings>>(p =>
        {
            AddDefaults(p);
            p.Add(x => x.IsFirst, true);
        });

        var classAttr = cut.Find(".card").GetAttribute("class");
        Assert.DoesNotContain("mt-4", classAttr);
    }

    [Fact]
    public void IsFirstFalse_IncludesTopMarginClass()
    {
        var cut = Render<ConnectionSettingsCard<TestSettings>>(AddDefaults);

        var classAttr = cut.Find(".card").GetAttribute("class");
        Assert.Contains("mt-4", classAttr);
    }

    [Fact]
    public void ExtraContent_RenderedAfterSavedAlert()
    {
        var cut = Render<ConnectionSettingsCard<TestSettings>>(p =>
        {
            AddDefaults(p);
            p.Add(x => x.ExtraContent, "<div class=\"library-picker-marker\">extra</div>");
        });

        Assert.NotEmpty(cut.FindAll(".library-picker-marker"));
    }

    [Fact]
    public void FormFields_AreRenderedInsideTheForm()
    {
        var cut = Render<ConnectionSettingsCard<TestSettings>>(p =>
        {
            AddDefaults(p);
            p.Add(x => x.FormFields, "<input class=\"my-field\" />");
        });

        Assert.NotEmpty(cut.Find("form").QuerySelectorAll(".my-field"));
    }
}
