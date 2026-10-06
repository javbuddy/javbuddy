using Javbuddy.Data;
using Javbuddy.Services.HealthChecks;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Javbuddy.Tests.Services.HealthChecks;

public class DbHealthCheckTests
{
    private sealed class BrokenDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=" + Path.Combine(Path.GetTempPath(), "javbuddy-tests-" + Guid.NewGuid().ToString("N"), "missing-dir", "db.sqlite"))
            .Options;

        public AppDbContext CreateDbContext() => new(options);

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsHealthy_WhenDatabaseReachable()
    {
        using var factory = new TestDbContextFactory();
        var check = new DbHealthCheck(factory);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsUnhealthy_WhenDatabaseUnreachable()
    {
        var check = new DbHealthCheck(new BrokenDbContextFactory());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}
