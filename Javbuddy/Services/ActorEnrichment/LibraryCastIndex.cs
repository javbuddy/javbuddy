using Javbuddy.Data;
using Javbuddy.Services.Actors;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.ActorEnrichment;

/// <summary>Data loaded once per batch enrichment run and shared by its per-actor lookups, so a run over
/// every actor doesn't re-read the same library-wide rows for each one. A new run starts empty.</summary>
public sealed class ActorEnrichmentRunCache
{
    public LibraryCastIndex? LibraryCast { get; set; }
}

/// <summary>Every library movie's code indexed by the names in its cast text, for finding an actor's
/// candidate movies when none are linked yet.</summary>
public sealed class LibraryCastIndex
{
    private readonly List<string> codes = [];
    private readonly Dictionary<string, List<int>> moviesByName = new(StringComparer.OrdinalIgnoreCase);

    public LibraryCastIndex(IEnumerable<(string Code, string? Cast)> movies)
    {
        foreach (var (code, cast) in movies)
        {
            var position = codes.Count;
            codes.Add(code);
            foreach (var name in ActorMatching.SplitNames(cast))
            {
                if (!moviesByName.TryGetValue(name, out var positions))
                {
                    moviesByName[name] = positions = [];
                }
                if (positions.Count == 0 || positions[^1] != position) positions.Add(position);
            }
        }
    }

    public static async Task<LibraryCastIndex> LoadAsync(IDbContextFactory<AppDbContext> dbFactory, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movies = await db.Movies.AsNoTracking()
            .Where(m => m.Code != null && m.MetaActresses != null)
            .Select(m => new { m.Code, m.MetaActresses })
            .ToListAsync(ct);
        return new LibraryCastIndex(movies.Select(m => (m.Code!, m.MetaActresses)));
    }

    /// <summary>Codes of the movies whose cast includes any of these names (case-insensitive), each once,
    /// in library order.</summary>
    public List<string> CodesCasting(IEnumerable<string> names) =>
        names
            .SelectMany(name => moviesByName.GetValueOrDefault(name) ?? [])
            .Distinct()
            .Order()
            .Select(position => codes[position])
            .ToList();
}
