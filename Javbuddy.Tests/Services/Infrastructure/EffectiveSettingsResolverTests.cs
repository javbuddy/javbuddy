using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace Javbuddy.Tests.Services.Infrastructure;

public class SettingsResolverTests
{
    private static EffectiveSettingsResolver<JavinizerSettings> CreateResolver(TestDbContextFactory factory, IConfiguration configuration) =>
        new(
            factory,
            configuration,
            config => new JavinizerSettings { BaseUrl = config["Test:BaseUrl"], ApiToken = config["Test:ApiKey"] },
            (db, ct) => db.JavinizerSettings.FirstOrDefaultAsync(ct),
            s => !string.IsNullOrWhiteSpace(s.BaseUrl) && !string.IsNullOrWhiteSpace(s.ApiToken));

    [Fact]
    public async Task ResolveAsync_ReturnsEnvSettings_WhenEnvHasRequiredFields()
    {
        using var factory = new TestDbContextFactory();
        var configuration = Substitute.For<IConfiguration>();
        configuration["Test:BaseUrl"].Returns("http://env.test");
        configuration["Test:ApiKey"].Returns("env-key");

        var result = await CreateResolver(factory, configuration).ResolveAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("http://env.test", result!.BaseUrl);
    }

    [Fact]
    public async Task ResolveAsync_FallsBackToDb_WhenEnvIncomplete()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.JavinizerSettings.Add(new JavinizerSettings { BaseUrl = "http://db.test", ApiToken = "db-token" });
            await db.SaveChangesAsync();
        }

        var result = await CreateResolver(factory, Substitute.For<IConfiguration>()).ResolveAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("http://db.test", result!.BaseUrl);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNull_WhenNeitherEnvNorDbHaveRequiredFields()
    {
        using var factory = new TestDbContextFactory();

        var result = await CreateResolver(factory, Substitute.For<IConfiguration>()).ResolveAsync(CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNull_WhenDbRowMissingRequiredField()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.JavinizerSettings.Add(new JavinizerSettings { BaseUrl = "http://db.test", ApiToken = null });
            await db.SaveChangesAsync();
        }

        var result = await CreateResolver(factory, Substitute.For<IConfiguration>()).ResolveAsync(CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_ForwardsCallersTokenToTheDbQuery()
    {
        using var factory = new TestDbContextFactory();
        using var cts = new CancellationTokenSource();
        CancellationToken observed = default;
        var resolver = new EffectiveSettingsResolver<JavinizerSettings>(
            factory,
            Substitute.For<IConfiguration>(),
            _ => new JavinizerSettings(),
            (db, ct) =>
            {
                observed = ct;
                return db.JavinizerSettings.FirstOrDefaultAsync(ct);
            },
            s => !string.IsNullOrWhiteSpace(s.BaseUrl));

        await resolver.ResolveAsync(cts.Token);

        Assert.Equal(cts.Token, observed);
    }

    [Fact]
    public async Task ResolveAsync_CancelledDuringDbQuery_Throws()
    {
        using var factory = new TestDbContextFactory();
        using var cts = new CancellationTokenSource();
        var resolver = new EffectiveSettingsResolver<JavinizerSettings>(
            factory,
            Substitute.For<IConfiguration>(),
            _ => new JavinizerSettings(),
            (db, ct) =>
            {
                cts.Cancel();
                return db.JavinizerSettings.FirstOrDefaultAsync(ct);
            },
            s => !string.IsNullOrWhiteSpace(s.BaseUrl));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolver.ResolveAsync(cts.Token));
    }
}
