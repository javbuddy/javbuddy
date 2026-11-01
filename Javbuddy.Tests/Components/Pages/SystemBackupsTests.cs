using Bunit;
using Javbuddy.Components.Pages;
using Javbuddy.Models;
using Javbuddy.Services.Backups;
using Javbuddy.Services.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages;

public class SystemBackupsTests : BunitContext
{
    private readonly IBackupService backups = Substitute.For<IBackupService>();
    private readonly IBackupSettingsService settings = Substitute.For<IBackupSettingsService>();
    private readonly IHostApplicationLifetime lifetime = Substitute.For<IHostApplicationLifetime>();

    private static BackupEntry Entry(string name, BackupKind kind, int day, int movies = 12) => new(
        name, kind, 10_800_000,
        new BackupManifest("0.3.9", new DateTime(2026, 11, day, 4, 0, 0, DateTimeKind.Utc), "2026_Init", movies, 5, 7, 3));

    public SystemBackupsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        settings.GetStoredAsync(Arg.Any<CancellationToken>()).Returns(new BackupSettings());
        settings.GetEffectiveAsync(Arg.Any<CancellationToken>()).Returns(new BackupSettings());
        backups.ListAsync(Arg.Any<CancellationToken>()).Returns([
            Entry("javbuddy-auto-20261102-040000.zip", BackupKind.Auto, 2),
            Entry("javbuddy-manual-20261101-040000.zip", BackupKind.Manual, 1, movies: 1234),
        ]);
        Services.AddSingleton(backups);
        Services.AddSingleton(settings);
        Services.AddSingleton(lifetime);
    }

    [Fact]
    public void ListsEachBackupWithItsVersionSizeKindAndCounts()
    {
        var cut = Render<SystemBackups>();

        var rows = cut.FindAll(".backups-row");
        Assert.Equal(2, rows.Count);
        Assert.Contains("0.3.9", rows[0].TextContent);
        Assert.Contains("Automatic", rows[0].TextContent);
        Assert.Contains("10.3 MB", rows[0].TextContent);
        Assert.Contains("Manual", rows[1].TextContent);
        Assert.Contains("1,234 Movies", rows[1].TextContent);
        Assert.Contains("0 / 2", cut.Find(".backups-count").TextContent);
    }

    [Fact]
    public void ShowsTheStoredSettings_AndSavesEdits()
    {
        var cut = Render<SystemBackups>();
        Assert.Equal("10", cut.Find("#backup-retention").GetAttribute("value"));
        Assert.Equal("1", cut.Find("#backup-frequency").GetAttribute("value"));

        cut.Find("#backup-retention").Change("30");
        cut.Find("#backup-frequency").Change("7");
        cut.Find("#backup-save").Click();

        settings.Received(1).SaveAsync(Arg.Is<BackupSettings>(s => s.RetentionDays == 30 && s.FrequencyDays == 7 && s.Enabled), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void RejectsAnInvalidNumberOfDays_WithoutSaving()
    {
        var cut = Render<SystemBackups>();

        cut.Find("#backup-frequency").Change("0");
        cut.Find("#backup-save").Click();

        Assert.Contains("between 1 and 36500 days", cut.Find(".alert-danger").TextContent);
        settings.DidNotReceive().SaveAsync(Arg.Any<BackupSettings>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void RejectsMoreDaysThanTheMaximum_WithoutSaving()
    {
        var cut = Render<SystemBackups>();

        cut.Find("#backup-retention").Change("999999");
        cut.Find("#backup-save").Click();

        Assert.Contains("36500", cut.Find(".alert-danger").TextContent);
        settings.DidNotReceive().SaveAsync(Arg.Any<BackupSettings>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Save_ShowsAFailureInsteadOfEndingTheCircuit()
    {
        settings.SaveAsync(Arg.Any<BackupSettings>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("database is locked")));
        var cut = Render<SystemBackups>();

        cut.Find("#backup-save").Click();

        Assert.Contains("database is locked", cut.Find(".alert-danger").TextContent);
        Assert.False(cut.Find("#backup-save").HasAttribute("disabled"));
    }

    [Fact]
    public void Filtering_DropsSelectedBackupsItHides_SoDeleteNeverRemovesThemOutOfView()
    {
        backups.DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(OperationResult.Ok());
        var cut = Render<SystemBackups>();
        cut.Find(".backups-count input").Change(true);

        cut.Find(".backups-filter").Input("manual");
        cut.Find("#backup-delete").Click();
        cut.Find(".delete-confirm-confirm-btn").Click();

        backups.Received(1).DeleteAsync("javbuddy-manual-20261101-040000.zip", Arg.Any<CancellationToken>());
        backups.DidNotReceive().DeleteAsync("javbuddy-auto-20261102-040000.zip", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AFieldSetByEnvironmentIsReadOnlyAndShowsTheEffectiveValue()
    {
        settings.IsSetByEnvironment(nameof(BackupSettings.FrequencyDays)).Returns(true);
        settings.GetEffectiveAsync(Arg.Any<CancellationToken>()).Returns(new BackupSettings { FrequencyDays = 3 });

        var cut = Render<SystemBackups>();

        var input = cut.Find("#backup-frequency");
        Assert.True(input.HasAttribute("disabled"));
        Assert.Equal("3", input.GetAttribute("value"));
        Assert.Contains("Backup__FrequencyDays", cut.Markup);
    }

    [Fact]
    public void BackupNow_CreatesAManualBackup()
    {
        backups.CreateAsync(BackupKind.Manual, Arg.Any<CancellationToken>()).Returns(Entry("javbuddy-manual-20261103-040000.zip", BackupKind.Manual, 3));
        var cut = Render<SystemBackups>();

        cut.Find("#backup-now").Click();

        backups.Received(1).CreateAsync(BackupKind.Manual, Arg.Any<CancellationToken>());
        Assert.Contains("Backup created", cut.Find(".alert-success").TextContent);
    }

    [Fact]
    public void RestoreAndExport_NeedExactlyOneSelectedBackup()
    {
        var cut = Render<SystemBackups>();
        Assert.True(cut.Find("#backup-restore").HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("#backup-export"));

        cut.FindAll(".backups-row input[type=checkbox]")[0].Change(true);
        Assert.False(cut.Find("#backup-restore").HasAttribute("disabled"));
        Assert.Equal("api/backups/javbuddy-auto-20261102-040000.zip/download", cut.Find("#backup-export").GetAttribute("href"));

        cut.FindAll(".backups-row input[type=checkbox]")[1].Change(true);
        Assert.True(cut.Find("#backup-restore").HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("#backup-export"));
        Assert.False(cut.Find("#backup-delete").HasAttribute("disabled"));
    }

    [Fact]
    public void Restore_AsksForConfirmation_ThenStagesItAndOffersARestart()
    {
        backups.StageRestoreAsync("javbuddy-auto-20261102-040000.zip", Arg.Any<CancellationToken>()).Returns(OperationResult.Ok());
        var cut = Render<SystemBackups>();
        cut.FindAll(".backups-row input[type=checkbox]")[0].Change(true);

        cut.Find("#backup-restore").Click();
        backups.DidNotReceive().StageRestoreAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        cut.Find("#backup-restore-confirm").Click();

        backups.Received(1).StageRestoreAsync("javbuddy-auto-20261102-040000.zip", Arg.Any<CancellationToken>());
        Assert.Contains("A restore is staged", cut.Find(".alert-warning").TextContent);

        cut.Find(".alert-warning button").Click();
        lifetime.Received(1).StopApplication();
    }

    [Fact]
    public void Restore_ShowsWhyABackupCannotBeRestored()
    {
        backups.StageRestoreAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(OperationResult.Fail("made by a newer version"));
        var cut = Render<SystemBackups>();
        cut.FindAll(".backups-row input[type=checkbox]")[0].Change(true);

        cut.Find("#backup-restore").Click();
        cut.Find("#backup-restore-confirm").Click();

        Assert.Contains("made by a newer version", cut.Find(".alert-danger").TextContent);
        Assert.Empty(cut.FindAll(".alert-warning"));
    }

    [Fact]
    public void Delete_RemovesEverySelectedBackupAfterConfirming()
    {
        backups.DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(OperationResult.Ok());
        var cut = Render<SystemBackups>();
        cut.Find(".backups-count input").Change(true);

        cut.Find("#backup-delete").Click();
        backups.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        cut.Find(".delete-confirm-confirm-btn").Click();

        backups.Received(1).DeleteAsync("javbuddy-auto-20261102-040000.zip", Arg.Any<CancellationToken>());
        backups.Received(1).DeleteAsync("javbuddy-manual-20261101-040000.zip", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Filter_NarrowsTheList()
    {
        var cut = Render<SystemBackups>();

        cut.Find(".backups-filter").Input("manual");

        Assert.Single(cut.FindAll(".backups-row"));
    }

    [Fact]
    public void ShowsTheRestartBannerWhenARestoreIsAlreadyStaged()
    {
        backups.RestorePending.Returns(true);

        var cut = Render<SystemBackups>();

        Assert.Contains("A restore is staged", cut.Find(".alert-warning").TextContent);
    }
}
