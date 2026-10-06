using Javbuddy.Data;
using Javbuddy.Services.R18Dev;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Tasks;

/// <summary>Downloads and imports the r18.dev database dump into its sidecar SQLite file (see
/// R18DevDumpImporter), on a weekly interval matching r18.dev's own Tuesday publish cadence.
/// A no-op — skipped, not run — while the r18.dev metadata source is disabled in Settings, since
/// the whole feature is opt-in.</summary>
public class R18DevImportTask(IR18DevDumpImporter importer, IDbContextFactory<AppDbContext> dbFactory) : IScheduledTask
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(7);

    private readonly IR18DevDumpImporter importer = importer;
    private readonly IDbContextFactory<AppDbContext> dbFactory = dbFactory;

    public string Name => "r18.dev Dump Import";

    public string Description =>
        "Downloads and imports the weekly r18.dev database dump for local metadata lookups. Skipped " +
        "while the r18.dev metadata source is disabled in Settings.";

    public TimeSpan GetInterval() => Interval;

    public async Task<string?> RunAsync(CancellationToken ct, IProgress<TaskProgress> progress)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await db.R18DevSettings.OrderBy(s => s.Id).FirstOrDefaultAsync(ct);
        if (settings is not { Enabled: true })
        {
            return "skipped — r18.dev metadata source is disabled";
        }

        var result = await importer.ImportAsync(progress, ct);

        settings.LastImportedAt = DateTime.UtcNow;
        settings.LastImportSourceDate = result.SourceDate;
        settings.LastImportSummary = result.Summary;
        await db.SaveChangesAsync(ct);

        if (!result.Success)
        {
            throw new InvalidOperationException(result.ErrorMessage ?? "r18.dev import failed");
        }

        return result.Summary;
    }
}
