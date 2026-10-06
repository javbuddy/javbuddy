using Javbuddy.Data;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Actors;

/// <summary>Helpers for parsing the metadata cast summary, rendering actor placeholders, and
/// matching a free-text name against a tracked actor's known identities.</summary>
public static class ActorMatching
{
    private static readonly char[] NameDelimiters = [',', '、', ';'];

    public static IEnumerable<string> SplitNames(string? metaActresses) =>
        (metaActresses ?? string.Empty)
            .Split(NameDelimiters, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>One or two uppercase initials for an avatar placeholder, e.g. "Yui Hatano" → "YH".</summary>
    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant()
        };
    }

    /// <summary>A tracked actor's known identities (display name, reversed order, Japanese
    /// kanji/kana, R18Dev name, aliases) for matching against free-text names from metadata or an
    /// external source.</summary>
    public sealed record ActorLookup(
        int Id,
        string DisplayName,
        string? ReversedName,
        string? JapaneseNameKanji,
        string? JapaneseNameKana,
        string? R18DevName,
        IReadOnlyList<string>? Aliases = null)
    {
        public bool Matches(IReadOnlySet<string> names)
        {
            if (names.Contains(DisplayName)) return true;
            if (ReversedName is not null && names.Contains(ReversedName)) return true;
            if (R18DevName is not null && names.Contains(R18DevName)) return true;

            if (JapaneseNameKanji is not null)
            {
                if (names.Contains(JapaneseNameKanji)) return true;
                var compact = JapaneseNameKanji.Replace(" ", "");
                if (names.Any(n => n.Replace(" ", "").Equals(compact, StringComparison.OrdinalIgnoreCase))) return true;
            }

            if (JapaneseNameKana is not null)
            {
                if (names.Contains(JapaneseNameKana)) return true;
                var compact = JapaneseNameKana.Replace(" ", "");
                if (names.Any(n => n.Replace(" ", "").Equals(compact, StringComparison.OrdinalIgnoreCase))) return true;
            }

            if (Aliases is not null)
            {
                foreach (var alias in Aliases)
                {
                    if (names.Contains(alias)) return true;
                    var compact = alias.Replace(" ", "");
                    if (names.Any(n => n.Replace(" ", "").Equals(compact, StringComparison.OrdinalIgnoreCase))) return true;
                }
            }

            return false;
        }
    }

    /// <summary>Case-insensitive name → actor index over a fixed list of <see cref="ActorLookup"/>s,
    /// built once so matching many names costs a couple of dictionary lookups each instead of a
    /// scan over every actor. Gives exactly the results of
    /// <see cref="ActorLookup.Matches"/> against a single-name set: display, reversed and R18Dev
    /// names match whole; kanji, kana and aliases match ignoring spaces (which also covers their
    /// whole-name match).</summary>
    public sealed class ActorIndex
    {
        private readonly IReadOnlyList<ActorLookup> _actors;
        // Values are positions in _actors, ascending, so the first one is the first match in list order.
        private readonly Dictionary<string, List<int>> _byName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<int>> _byCompactName = new(StringComparer.OrdinalIgnoreCase);

        public ActorIndex(IReadOnlyList<ActorLookup> actors)
        {
            _actors = actors;
            for (var i = 0; i < actors.Count; i++)
            {
                var actor = actors[i];
                Add(_byName, actor.DisplayName, i);
                Add(_byName, actor.ReversedName, i);
                Add(_byName, actor.R18DevName, i);
                Add(_byCompactName, Compact(actor.JapaneseNameKanji), i);
                Add(_byCompactName, Compact(actor.JapaneseNameKana), i);
                foreach (var alias in actor.Aliases ?? [])
                {
                    Add(_byCompactName, Compact(alias), i);
                }
            }
        }

        /// <summary>The first actor, in list order, whose known identities match <paramref name="name"/>.</summary>
        public ActorLookup? FindMatch(string name)
        {
            var byName = _byName.GetValueOrDefault(name);
            var byCompactName = _byCompactName.GetValueOrDefault(Compact(name)!);
            if (byName is null && byCompactName is null) return null;
            var first = Math.Min(byName?[0] ?? int.MaxValue, byCompactName?[0] ?? int.MaxValue);
            return _actors[first];
        }

        /// <summary>Adds the id of every actor matching <paramref name="name"/> to
        /// <paramref name="actorIds"/>; returns whether any did.</summary>
        public bool AddMatchingIds(string name, ISet<int> actorIds)
        {
            var matched = AddIds(_byName.GetValueOrDefault(name), actorIds);
            return AddIds(_byCompactName.GetValueOrDefault(Compact(name)!), actorIds) || matched;
        }

        private bool AddIds(List<int>? positions, ISet<int> actorIds)
        {
            if (positions is null) return false;
            foreach (var position in positions)
            {
                actorIds.Add(_actors[position].Id);
            }
            return true;
        }

        private static void Add(Dictionary<string, List<int>> index, string? key, int position)
        {
            if (key is null) return;
            if (!index.TryGetValue(key, out var positions))
            {
                index[key] = [position];
            }
            else if (positions[^1] != position)
            {
                positions.Add(position);
            }
        }

        private static string? Compact(string? name) => name?.Replace(" ", "");
    }

    public static async Task<List<ActorLookup>> LoadActorLookupsAsync(AppDbContext db, CancellationToken ct = default) =>
        (await db.Actors
            .Where(actor => actor.FirstName != null)
            .Select(actor => new
            {
                actor.Id,
                actor.FirstName,
                actor.LastName,
                actor.JapaneseNameKanji,
                actor.JapaneseNameKana,
                actor.R18DevName,
                Aliases = actor.Aliases.Select(a => a.Name).ToList()
            })
            .ToListAsync(ct))
            .Select(actor => new ActorLookup(
                actor.Id,
                ActorDisplayName.Format(actor.FirstName!, actor.LastName),
                !string.IsNullOrWhiteSpace(actor.LastName) ? $"{actor.FirstName} {actor.LastName}" : null,
                actor.JapaneseNameKanji,
                actor.JapaneseNameKana,
                actor.R18DevName,
                actor.Aliases))
            .ToList();
}
