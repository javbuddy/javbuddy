using Javbuddy.Models;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Tags;

/// <summary>An actor-tag filter's options, for the Movies grid and the Scenes wall.</summary>
public static class ActorTagOptions
{
    /// <summary>Between a subtag's parent and name in an option's label; the filter menus nest on it.</summary>
    public const string LabelSeparator = " › ";

    /// <summary>The used tags, "Hair › Long" for a subtag, plus the parents of those (a parent matches its subtags in the
    /// filters). Grouped by parent, the parent first and its subtags after it: ordering by the full label would slip
    /// "Hair colour" between "Hair" and "Hair › Short".</summary>
    public static async Task<List<(int Id, string Label)>> LoadAsync(IQueryable<Tag> usedTags, CancellationToken ct)
    {
        var used = await usedTags
            .Select(t => new { t.Id, t.Name, t.ParentTagId, Parent = t.ParentTag != null ? t.ParentTag.Name : null })
            .Distinct()
            .ToListAsync(ct);
        var rows = used.Select(t => (Id: t.Id, Label: t.Parent is null ? t.Name : $"{t.Parent}{LabelSeparator}{t.Name}", Group: t.Parent ?? t.Name, IsSubtag: t.Parent is not null, t.Name)).ToList();
        rows.AddRange(used.Where(t => t.ParentTagId is not null && used.All(u => u.Id != t.ParentTagId))
            .DistinctBy(t => t.ParentTagId)
            .Select(t => (Id: t.ParentTagId!.Value, Label: t.Parent!, Group: t.Parent!, IsSubtag: false, Name: t.Parent!)));
        return [.. rows
            .OrderBy(r => r.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.IsSubtag)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(r => (r.Id, r.Label))];
    }
}
