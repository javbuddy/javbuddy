using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Javbuddy.Services.MediaInfo;

/// <summary>The restoration settings Jasna (https://github.com/Kruk2/jasna) writes into each output's
/// container <c>comment</c> tag: one line of JSON whose first key is <c>"jasna"</c> (the version),
/// then <c>schema</c>, <c>detection</c>, one block per restoration model used (<c>basicvsrpp</c>,
/// <c>ltx</c>), optional <c>auto_split_percent</c> / <c>segments</c>, <c>output</c>, and
/// <c>source_comment</c> (the source file's own comment, kept). Contract from Jasna's
/// <c>jasna/settings_tag.py</c>, schema 1.</summary>
/// <param name="Json">The tag exactly as read from the file.</param>
/// <param name="Version">Jasna's version, e.g. "0.12.3".</param>
/// <param name="Schema">The tag's schema number, when present.</param>
/// <param name="Restoration">The restoration models' keys, sorted and joined with "+", e.g. "ltx" or
/// "basicvsrpp+ltx"; null when the tag names none.</param>
public sealed record JasnaSettings(string Json, string Version, int? Schema, string? Restoration);

public static class JasnaSettingsTag
{
    /// <summary>Jasna's own settings stay under 2 000 characters, but it keeps the source file's own
    /// comment as <c>source_comment</c>, cut at 16 000 characters before Python's <c>json.dumps</c>
    /// escapes it, and non-ASCII text (a Japanese comment) escapes to 6 characters per character
    /// (<c>\uXXXX</c>). Anything bigger than that is not a Jasna tag worth storing.</summary>
    public const int MaxLength = 16_000 * 6 + 4_000;

    // Top-level keys that aren't a restoration model's block.
    private static readonly HashSet<string> NonModelKeys = ["jasna", "schema", "detection", "auto_split_percent", "segments", "output", "source_comment"];

    private static readonly Dictionary<string, string> ModelNames = new(StringComparer.Ordinal)
    {
        ["ltx"] = "LTX",
        ["basicvsrpp"] = "BasicVSR++",
    };

    /// <summary>Parses a file's comment tag. Null for an empty, oversized, non-JSON or non-Jasna
    /// comment (a JSON object without a string <c>jasna</c> key) — never throws.</summary>
    public static JasnaSettings? Parse(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment) || comment.Length > MaxLength) return null;
        var trimmed = comment.Trim();
        if (!trimmed.StartsWith('{')) return null;

        // JsonNode builds an object's key lookup lazily: a duplicate key throws ArgumentException on
        // the first access, not in Parse, so every access stays inside the try.
        try
        {
            if (JsonNode.Parse(trimmed) is not JsonObject root
                || root["jasna"] is not JsonValue versionNode
                || !versionNode.TryGetValue<string>(out var version)
                || string.IsNullOrWhiteSpace(version))
            {
                return null;
            }

            int? schema = root["schema"] is JsonValue schemaNode && schemaNode.TryGetValue<int>(out var s) ? s : null;
            var models = ModelKeys(root).Order(StringComparer.Ordinal).ToList();
            return new JasnaSettings(trimmed, version.Trim(), schema, models.Count > 0 ? string.Join('+', models) : null);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Display name for a stored <see cref="JasnaSettings.Restoration"/> value, e.g.
    /// "basicvsrpp+ltx" → "BasicVSR++ + LTX".</summary>
    public static string? RestorationLabel(string? restoration) =>
        string.IsNullOrWhiteSpace(restoration) ? null : string.Join(" + ", restoration.Split('+').Select(ModelName));

    /// <summary>The tag as report sections ("Jasna", one per restoration model, "Jasna: Output"),
    /// each a list of label/value lines in the tag's own order. Unknown keys and models (a later
    /// schema) are shown generically; output settings left at their off value are skipped.</summary>
    public static IReadOnlyList<(string Title, IReadOnlyList<(string Label, string Value)> Lines)> ReportSections(string json)
    {
        if (Parse(json) is not { } settings)
        {
            return [];
        }

        // Parse only reads the top level; a duplicate key inside a nested block throws on first
        // access, so a malformed tag shows no Jasna sections rather than breaking the modal.
        try
        {
            return JsonNode.Parse(settings.Json) is JsonObject root ? BuildSections(settings, root) : [];
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return [];
        }
    }

    private static List<(string Title, IReadOnlyList<(string Label, string Value)> Lines)> BuildSections(JasnaSettings settings, JsonObject root)
    {

        var sections = new List<(string, IReadOnlyList<(string, string)>)>();
        var main = new List<(string, string)>
        {
            ("Version", settings.Schema is { } schema ? $"{settings.Version} (schema {schema})" : settings.Version),
        };
        if (RestorationLabel(settings.Restoration) is { } restoration) main.Add(("Restoration", restoration));
        if (root["detection"] is JsonObject detection) AddObject(main, detection, "Detection ");
        if (Scalar(root["auto_split_percent"]) is { } autoSplit) main.Add(("Auto split", autoSplit + "%"));
        if (root["segments"] is JsonObject segments) AddSegments(main, segments);
        foreach (var (key, value) in root)
        {
            if (NonModelKeys.Contains(key) || value is JsonObject) continue;
            if (Render(value) is { } text) main.Add((Humanize(key), text));
        }
        if (Scalar(root["source_comment"]) is { } sourceComment) main.Add(("Source comment", Shorten(sourceComment, 200)));
        sections.Add(("Jasna", main));

        foreach (var key in ModelKeys(root))
        {
            var lines = new List<(string, string)>();
            AddObject(lines, (JsonObject)root[key]!, "");
            if (lines.Count > 0) sections.Add(($"Jasna: {ModelName(key)}", lines));
        }

        if (root["output"] is JsonObject output)
        {
            var lines = new List<(string, string)>();
            var codec = Scalar(output["codec"]);
            var encoder = Scalar(output["encoder"]);
            if (codec is not null || encoder is not null)
            {
                lines.Add(("Codec", codec is not null && encoder is not null ? $"{codec} ({encoder})" : codec ?? encoder!));
            }
            foreach (var (key, value) in output)
            {
                if (key is "codec" or "encoder" or "encoder_options") continue;
                if (IsOff(value)) continue;
                if (Render(value) is { } text) lines.Add((Humanize(key), text));
            }
            if (output["encoder_options"] is JsonObject options && Render(options) is { } encoderSettings)
            {
                lines.Add(("Encoder settings", encoderSettings));
            }
            if (lines.Count > 0) sections.Add(("Jasna: Output", lines));
        }

        return sections;
    }

    private static IEnumerable<string> ModelKeys(JsonObject root) =>
        root.Where(p => !NonModelKeys.Contains(p.Key) && p.Value is JsonObject).Select(p => p.Key);

    private static string ModelName(string key) => ModelNames.TryGetValue(key, out var name) ? name : key;

    private static void AddObject(List<(string, string)> lines, JsonObject obj, string labelPrefix)
    {
        foreach (var (key, value) in obj)
        {
            if (Render(value) is { } text) lines.Add((labelPrefix + (labelPrefix.Length > 0 ? Humanize(key).ToLowerInvariant() : Humanize(key)), text));
        }
    }

    // "segments": {"count": 3, "ltx": {"count": 2, "seconds": 12.5, "seeds": [1, 2]}, ...}
    private static void AddSegments(List<(string, string)> lines, JsonObject segments)
    {
        if (Scalar(segments["count"]) is { } count) lines.Add(("Segments", count));
        foreach (var (key, value) in segments)
        {
            if (key == "count") continue;
            if (Render(value) is { } text) lines.Add(($"Segments ({ModelName(key)})", text));
        }
    }

    /// <summary>A value as report text: strings as-is, booleans Yes/No, numbers invariant, arrays
    /// comma-joined, objects "key=value / key=value". Null and empty values give null.</summary>
    private static string? Render(JsonNode? node) => node switch
    {
        null => null,
        JsonObject obj => Join(obj.Select(p => Render(p.Value) is { } v ? $"{p.Key}={v}" : null), " / "),
        JsonArray array => Join(array.Select(Render), ", "),
        JsonValue => Scalar(node),
        _ => null,
    };

    private static string? Join(IEnumerable<string?> parts, string separator)
    {
        var list = parts.Where(p => p is not null).ToList();
        return list.Count > 0 ? string.Join(separator, list) : null;
    }

    private static string? Scalar(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        return value.GetValueKind() switch
        {
            JsonValueKind.String => value.GetValue<string>() is { Length: > 0 } s ? s : null,
            JsonValueKind.True => "Yes",
            JsonValueKind.False => "No",
            JsonValueKind.Number => value.TryGetValue<double>(out var d) ? d.ToString(CultureInfo.InvariantCulture) : value.ToJsonString(),
            _ => null,
        };
    }

    // An output setting left off: null, false, 0 or "none" (lut, sharpen, upscale, frame generation...).
    private static bool IsOff(JsonNode? node) => node switch
    {
        null => true,
        JsonValue value => value.GetValueKind() switch
        {
            JsonValueKind.False or JsonValueKind.Null => true,
            JsonValueKind.Number => value.TryGetValue<double>(out var d) && d == 0,
            JsonValueKind.String => string.Equals(value.GetValue<string>(), "none", StringComparison.OrdinalIgnoreCase),
            _ => false,
        },
        _ => false,
    };

    // Words Jasna's keys use that read as initialisms ("vae_encoder" → "VAE encoder").
    private static readonly HashSet<string> Initialisms = ["vae", "lut", "fps", "vr", "rtx", "tvai"];

    private static string Humanize(string key)
    {
        var words = key.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => Initialisms.Contains(w) ? w.ToUpperInvariant() : w)
            .ToArray();
        var text = string.Join(' ', words);
        return text.Length > 0 ? char.ToUpperInvariant(text[0]) + text[1..] : text;
    }

    private static string Shorten(string text, int max)
    {
        var singleLine = text.ReplaceLineEndings(" ");
        return singleLine.Length <= max ? singleLine : singleLine[..max] + "…";
    }
}
