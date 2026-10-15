using Javbuddy.Data;

namespace Javbuddy.Services.Tags;

/// <summary>A tag on something a tag filter keeps or drops (a movie, scene, highlight or apex), and who holds it:
/// an actor for an actor tag, 0 for the owner's own tag.</summary>
public sealed class TaggedRow
{
    public int OwnerId { get; init; }
    public int HolderId { get; init; }
    public int TagId { get; init; }
}

/// <summary>The tag-filter rules the Movies grid and the Scenes wall share: a selected parent tag matches its subtags,
/// and a filter matches any selected tag or, with match-all, every one of them.</summary>
public static class TagFilterQueries
{
    /// <summary>The selected tags and their subtags.</summary>
    public static IQueryable<int> MatchingTagIds(AppDbContext db, IReadOnlyCollection<int> tagIds) =>
        db.Tags.Where(t => tagIds.Contains(t.Id) || (t.ParentTagId != null && tagIds.Contains(t.ParentTagId.Value))).Select(t => t.Id);

    /// <summary>Owners with a row of a selected tag (or a subtag), or with <paramref name="matchAll"/> a holder that has
    /// every selected tag. Both are one non-correlated IN (…) source, so SQLite evaluates <paramref name="rows"/> once
    /// whatever the number of tags.</summary>
    public static IQueryable<int> OwnersWith(AppDbContext db, IQueryable<TaggedRow> rows, IReadOnlyCollection<int> tagIds, bool matchAll)
    {
        if (!matchAll || tagIds.Count < 2)
        {
            var matching = MatchingTagIds(db, tagIds);
            return rows.Where(r => matching.Contains(r.TagId)).Select(r => r.OwnerId);
        }
        // Each matching tag paired with the selected tag it counts for; a subtag counts for its parent and, when
        // both are selected, for itself too.
        var selected = tagIds.Distinct().ToList();
        var counted = db.Tags.Where(t => selected.Contains(t.Id)).Select(t => new { t.Id, SelectedId = t.Id })
            .Concat(db.Tags.Where(t => t.ParentTagId != null && selected.Contains(t.ParentTagId.Value)).Select(t => new { t.Id, SelectedId = t.ParentTagId!.Value }));
        var needed = selected.Count;
        return from r in rows
               join c in counted on r.TagId equals c.Id
               group c.SelectedId by new { r.OwnerId, r.HolderId } into g
               where g.Distinct().Count() == needed
               select g.Key.OwnerId;
    }
}
