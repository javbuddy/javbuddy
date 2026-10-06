using Javbuddy.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Javbuddy.Services.HealthChecks;

/// <summary>Backs the k8s readiness probe (/healthz/ready) — reports unhealthy if
/// the SQLite database isn't reachable. Deliberately not wired into the liveness probe: a
/// briefly slow/locked DB should take the pod out of load-balancer rotation, not restart it.</summary>
public class DbHealthCheck(IDbContextFactory<AppDbContext> dbFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Cannot connect to the database.");
    }
}
