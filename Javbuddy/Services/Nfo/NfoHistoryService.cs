using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.LocalLibrary;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Nfo;

/// <summary>An earlier version of a movie's .nfo, as listed in the Edit .nfo modal's history.</summary>
public sealed record NfoGenerationItem(int Id, DateTime ReplacedAtUtc, NfoWriteTrigger Trigger, string Content);

public interface INfoHistoryService
{
    /// <summary>The movie's stored .nfo versions, newest first.</summary>
    Task<IReadOnlyList<NfoGenerationItem>> GetGenerationsAsync(int movieId, CancellationToken ct = default);

    /// <summary>Overwrites the movie's .nfo with <paramref name="content"/> (see
    /// ILocalLibraryClient.SaveRawNfoAsync) and keeps the replaced content as a generation.
    /// Returns false if the .nfo couldn't be found or written.</summary>
    Task<bool> SaveAsync(int movieId, string content, NfoWriteTrigger trigger = NfoWriteTrigger.ManualEdit, CancellationToken ct = default);

    /// <summary>Writes a stored generation back to the movie's .nfo; the content it replaces
    /// becomes a new generation itself, so a restore can be undone. Returns the restored content,
    /// or null if the generation or the .nfo wasn't found or couldn't be written.</summary>
    Task<string?> RestoreAsync(int movieId, int generationId, CancellationToken ct = default);
}

/// <summary>Keeps each movie's earlier .nfo versions in the database, in generations,
/// instead of a ".nfo.bak" next to the file in the media folder. Every Javbuddy .nfo write goes
/// through NfoFileWriter, which returns the content it replaced; the writer's caller stores it via
/// <see cref="AddGenerationAsync"/>. History is lost with the database, which is accepted.</summary>
public class NfoHistoryService(IDbContextFactory<AppDbContext> dbFactory, ILocalLibraryClient localLibraryClient) : INfoHistoryService
{
    public const int GenerationsKept = 5;

    /// <summary>Stages <paramref name="previousContent"/> as the movie's newest generation on
    /// <paramref name="db"/> and removes the ones beyond <see cref="GenerationsKept"/>; the caller's
    /// SaveChangesAsync persists both. Nothing is staged for a write that didn't change the file.</summary>
    public static async Task AddGenerationAsync(
        AppDbContext db,
        int movieId,
        string? previousContent,
        string? newContent,
        NfoWriteTrigger trigger,
        CancellationToken ct)
    {
        if (previousContent is null || string.Equals(previousContent, newContent, StringComparison.Ordinal)) return;

        var stale = await db.NfoGenerations
            .Where(g => g.MovieId == movieId)
            .OrderByDescending(g => g.ReplacedAtUtc)
            .ThenByDescending(g => g.Id)
            .Skip(GenerationsKept - 1)
            .ToListAsync(ct);
        db.NfoGenerations.RemoveRange(stale);

        db.NfoGenerations.Add(new NfoGeneration
        {
            MovieId = movieId,
            ReplacedAtUtc = DateTime.UtcNow,
            Trigger = trigger,
            Content = previousContent,
        });
    }

    public async Task<IReadOnlyList<NfoGenerationItem>> GetGenerationsAsync(int movieId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.NfoGenerations
            .AsNoTracking()
            .Where(g => g.MovieId == movieId)
            .OrderByDescending(g => g.ReplacedAtUtc)
            .ThenByDescending(g => g.Id)
            .Select(g => new NfoGenerationItem(g.Id, g.ReplacedAtUtc, g.Trigger, g.Content))
            .ToListAsync(ct);
    }

    public async Task<bool> SaveAsync(int movieId, string content, NfoWriteTrigger trigger = NfoWriteTrigger.ManualEdit, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var code = await db.Movies.Where(m => m.Id == movieId).Select(m => m.Code).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(code)) return false;

        var previousContent = await localLibraryClient.SaveRawNfoAsync(code, content, ct);
        if (previousContent is null) return false;

        await AddGenerationAsync(db, movieId, previousContent, content, trigger, ct);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<string?> RestoreAsync(int movieId, int generationId, CancellationToken ct = default)
    {
        string? content;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            content = await db.NfoGenerations
                .Where(g => g.Id == generationId && g.MovieId == movieId)
                .Select(g => g.Content)
                .FirstOrDefaultAsync(ct);
        }

        if (content is null) return null;
        return await SaveAsync(movieId, content, NfoWriteTrigger.Restore, ct) ? content : null;
    }
}
