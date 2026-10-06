using System.Text.RegularExpressions;
using System.Xml.Linq;
using Javbuddy.Models;
using Javbuddy.Services.Actors;

namespace Javbuddy.Services.Nfo;

/// <summary>How an .nfo's &lt;actor&gt; elements are matched to a tracked actor's names, consolidated onto the
/// canonical name, and mirrored into <c>Movie.MetaActresses</c> — pure functions shared by NfoSyncService,
/// NfoDriftDetector, MovieService and ActorService.</summary>
public static partial class NfoActorMatching
{
    public static HashSet<string> CreateNameMatcher(Actor actor, IReadOnlyList<string>? extraNames)
    {
        var matcher = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddName(string? n)
        {
            if (string.IsNullOrWhiteSpace(n)) return;
            var trimmed = n.Trim();
            matcher.Add(trimmed);
            var compact = trimmed.Replace(" ", "");
            if (compact.Length > 0) matcher.Add(compact);
        }

        AddName(actor.DisplayName);
        if (!string.IsNullOrWhiteSpace(actor.LastName))
        {
            AddName($"{actor.FirstName} {actor.LastName}");
            AddName($"{actor.LastName} {actor.FirstName}");
        }
        else
        {
            AddName(actor.FirstName);
        }

        AddName(actor.JapaneseNameKanji);
        AddName(actor.JapaneseNameKana);
        AddName(actor.R18DevName);

        if (actor.Aliases is not null)
        {
            foreach (var alias in actor.Aliases)
            {
                AddName(alias.Name);
            }
        }

        if (extraNames is not null)
        {
            foreach (var extra in extraNames)
            {
                AddName(extra);
            }
        }

        return matcher;
    }

    public static List<XElement> FindMatchingActorElements(XElement root, HashSet<string> nameMatcher, int linkedActorCount = 0)
    {
        var matching = new List<XElement>();

        foreach (var actorEl in root.Elements("actor"))
        {
            var name = actorEl.Element("name")?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(name) && Matches(name, nameMatcher))
            {
                matching.Add(actorEl);
                continue;
            }

            var altName = actorEl.Element("altname")?.Value?.Trim()
                ?? actorEl.Element("japanesename")?.Value?.Trim()
                ?? actorEl.Element("japanese_name")?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(altName) && Matches(altName, nameMatcher))
            {
                matching.Add(actorEl);
                continue;
            }

            foreach (var aliasEl in actorEl.Elements("alias"))
            {
                var v = aliasEl.Value?.Trim();
                if (!string.IsNullOrWhiteSpace(v) && Matches(v, nameMatcher))
                {
                    matching.Add(actorEl);
                    break;
                }
            }
        }

        // A single linked actor and a single on-disk actor represent the same person even when
        // no known name/alias matches. Every caller uses this policy so detection, proposal
        // generation, and direct sync agree.
        return matching.Count == 0 && linkedActorCount == 1 && root.Elements("actor").Take(2).Count() == 1
            ? [root.Elements("actor").First()]
            : matching;
    }

    public static bool Matches(string value, HashSet<string> nameMatcher)
    {
        if (nameMatcher.Contains(value)) return true;
        var compact = value.Replace(" ", "");
        if (compact.Length > 0 && nameMatcher.Contains(compact)) return true;

        // Strip trailing scraper disambiguation suffixes, e.g. "Hashimoto Arina2",
        // "Hashimoto Arina (2)", "Hashimoto Arina_2", "Hashimoto Arina #2",
        // "Hashimoto Arina（2）" — then retry.
        var stripped = TrailingDisambiguationRegex().Replace(value, "").Trim();
        if (stripped.Length > 0 && stripped != value)
        {
            if (nameMatcher.Contains(stripped)) return true;
            var strippedCompact = stripped.Replace(" ", "");
            if (strippedCompact.Length > 0 && nameMatcher.Contains(strippedCompact)) return true;
        }

        return false;
    }

    [GeneratedRegex(@"[ _\-\#]*[\(\[\{（【]?\d+[\)\]\}）】]?$", RegexOptions.Compiled)]
    private static partial Regex TrailingDisambiguationRegex();

    public static void ConsolidateAndUpdateActorElements(List<XElement> matchingActors, string canonicalName)
    {
        if (matchingActors.Count == 0) return;

        // Primary element: prefer one whose <name> is already canonical, otherwise the first
        var primary = matchingActors.FirstOrDefault(a => string.Equals(a.Element("name")?.Value?.Trim(), canonicalName, StringComparison.Ordinal))
            ?? matchingActors[0];

        // Ensure primary has <name> set to canonicalName
        var nameEl = primary.Element("name");
        if (nameEl is not null)
        {
            nameEl.Value = canonicalName;
        }
        else
        {
            primary.AddFirst(new XElement("name", canonicalName));
        }

        // For secondary duplicates: copy missing tags to primary, then remove secondary
        for (int i = 0; i < matchingActors.Count; i++)
        {
            var secondary = matchingActors[i];
            if (ReferenceEquals(secondary, primary)) continue;

            // Merge thumb, role, type if primary lacks them
            MergeChildElementIfMissing(primary, secondary, "thumb");
            MergeChildElementIfMissing(primary, secondary, "role");
            MergeChildElementIfMissing(primary, secondary, "type");

            // Remove secondary element and preceding whitespace text node if present
            var prevText = secondary.PreviousNode as XText;
            if (prevText is not null && string.IsNullOrWhiteSpace(prevText.Value))
            {
                prevText.Remove();
            }
            secondary.Remove();
        }
    }

    private static void MergeChildElementIfMissing(XElement primary, XElement secondary, string elementName)
    {
        var primaryEl = primary.Element(elementName);
        var secondaryEl = secondary.Element(elementName);
        if ((primaryEl is null || string.IsNullOrWhiteSpace(primaryEl.Value))
            && secondaryEl is not null && !string.IsNullOrWhiteSpace(secondaryEl.Value))
        {
            primary.Add(new XElement(secondaryEl));
        }
    }

    public static void UpdateMovieMetaActresses(Movie movie, HashSet<string> nameMatcher, string canonicalName, bool addIfMissing = true)
    {
        if (string.IsNullOrWhiteSpace(movie.MetaActresses))
        {
            if (addIfMissing)
            {
                movie.MetaActresses = canonicalName;
            }
            return;
        }

        var parts = ActorMatching.SplitNames(movie.MetaActresses).ToList();
        var updated = new List<string>();
        bool matched = false;

        foreach (var part in parts)
        {
            if (Matches(part, nameMatcher))
            {
                if (!updated.Contains(canonicalName, StringComparer.OrdinalIgnoreCase))
                {
                    updated.Add(canonicalName);
                }
                matched = true;
            }
            else
            {
                if (!updated.Contains(part, StringComparer.OrdinalIgnoreCase))
                {
                    updated.Add(part);
                }
            }
        }

        if (!matched && addIfMissing && !updated.Contains(canonicalName, StringComparer.OrdinalIgnoreCase))
        {
            updated.Add(canonicalName);
        }

        movie.MetaActresses = string.Join(", ", updated);
    }
}
