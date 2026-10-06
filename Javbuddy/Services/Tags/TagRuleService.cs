using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Scenes;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Tags;

public interface ITagRuleService
{
    Task<IReadOnlyList<TagReplacementRuleItem>> GetReplacementRulesAsync(CancellationToken ct = default);
    Task<TagOperationResult> AddReplacementRuleAsync(string sourceValue, TagMatchMode matchMode, int targetTagId, CancellationToken ct = default);
    Task DeleteReplacementRuleAsync(int ruleId, CancellationToken ct = default);
    Task<int> ApplyReplacementRulesToLibraryAsync(CancellationToken ct = default);

    Task<IReadOnlyList<IgnoredTagItem>> GetIgnoredTagsAsync(CancellationToken ct = default);
    Task<OperationResult> AddIgnoredTagAsync(string value, TagMatchMode matchMode, CancellationToken ct = default);
    Task RemoveIgnoredTagAsync(int id, CancellationToken ct = default);
    Task<int> StripIgnoredTagsFromLibraryAsync(CancellationToken ct = default);

    Task<bool> GetAutoIgnoreNonLatinAsync(CancellationToken ct = default);
    Task SetAutoIgnoreNonLatinAsync(bool enabled, CancellationToken ct = default);

    Task<int> RunDiscoveryAsync(CancellationToken ct = default);
}

/// <summary>The rules that shape the tag library automatically: replacement rules, the ignore list and the
/// non-Latin auto-ignore setting, and applying them to the whole library. clipActors, when present, wakes the
/// stored-actor refresh after a library pass marked every movie stale.</summary>
public class TagRuleService(IDbContextFactory<AppDbContext> dbFactory, ClipActorRefreshSignal? clipActors = null) : ITagRuleService
{
    public async Task<IReadOnlyList<TagReplacementRuleItem>> GetReplacementRulesAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.TagReplacementRules
            .AsNoTracking()
            .OrderBy(r => r.SourceValue)
            .Select(r => new TagReplacementRuleItem(r.Id, r.SourceValue, r.MatchMode, r.TargetTagId, r.TargetTag.Name, r.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<TagOperationResult> AddReplacementRuleAsync(string sourceValue, TagMatchMode matchMode, int targetTagId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceValue))
        {
            return TagOperationResult.Fail("Source value is required.");
        }

        var trimmed = sourceValue.Trim();
        if (trimmed.Length > 200)
        {
            return TagOperationResult.Fail("Source value cannot exceed 200 characters.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var target = await db.Tags.FirstOrDefaultAsync(t => t.Id == targetTagId, ct);
        if (target is null)
        {
            return TagOperationResult.Fail("Target tag not found.");
        }

        // The DB unique index on (SourceValue, MatchMode) only catches exact literal duplicates
        // (SQLite's default collation is case-sensitive) — check case-insensitively here too so a
        // CaseInsensitive-mode rule can't be duplicated by casing alone, mirroring ActorService's
        // AddAliasAsync collision checks.
        var duplicate = await db.TagReplacementRules.AnyAsync(r => r.MatchMode == matchMode && r.SourceValue.ToUpper() == trimmed.ToUpper(), ct);
        if (duplicate)
        {
            return TagOperationResult.Fail($"A rule for \"{trimmed}\" already exists.");
        }

        var rule = new TagReplacementRule { SourceValue = trimmed, MatchMode = matchMode, TargetTagId = target.Id };
        db.TagReplacementRules.Add(rule);
        await db.SaveChangesAsync(ct);

        return TagOperationResult.Ok(target);
    }

    public async Task DeleteReplacementRuleAsync(int ruleId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rule = await db.TagReplacementRules.FindAsync([ruleId], ct);
        if (rule is null) return;
        db.TagReplacementRules.Remove(rule);
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> ApplyReplacementRulesToLibraryAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var count = await TagNormalization.ApplyToLibraryAsync(db, ct);
        await db.SaveChangesAsync(ct);
        clipActors?.Signal();
        return count;
    }

    public async Task<IReadOnlyList<IgnoredTagItem>> GetIgnoredTagsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.IgnoredTags
            .AsNoTracking()
            .OrderBy(i => i.Value)
            .Select(i => new IgnoredTagItem(i.Id, i.Value, i.MatchMode, i.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<OperationResult> AddIgnoredTagAsync(string value, TagMatchMode matchMode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return OperationResult.Fail("Value is required.");
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 200)
        {
            return OperationResult.Fail("Value cannot exceed 200 characters.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var duplicate = await db.IgnoredTags.AnyAsync(i => i.MatchMode == matchMode && i.Value.ToUpper() == trimmed.ToUpper(), ct);
        if (duplicate)
        {
            return OperationResult.Fail($"\"{trimmed}\" is already on the ignore list.");
        }

        db.IgnoredTags.Add(new IgnoredTag { Value = trimmed, MatchMode = matchMode });
        await db.SaveChangesAsync(ct);

        return OperationResult.Ok();
    }

    public async Task RemoveIgnoredTagAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ignored = await db.IgnoredTags.FindAsync([id], ct);
        if (ignored is null) return;
        db.IgnoredTags.Remove(ignored);
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> StripIgnoredTagsFromLibraryAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var count = await TagNormalization.ApplyToLibraryAsync(db, ct);
        await db.SaveChangesAsync(ct);
        clipActors?.Signal();
        return count;
    }

    public async Task<bool> GetAutoIgnoreNonLatinAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await db.TagSettings.AsNoTracking().OrderBy(s => s.Id).FirstOrDefaultAsync(ct);
        return settings?.AutoIgnoreNonLatinTags ?? false;
    }

    public async Task SetAutoIgnoreNonLatinAsync(bool enabled, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await db.TagSettings.OrderBy(s => s.Id).FirstOrDefaultAsync(ct);
        if (settings is null)
        {
            db.TagSettings.Add(new TagSettings { AutoIgnoreNonLatinTags = enabled });
        }
        else
        {
            settings.AutoIgnoreNonLatinTags = enabled;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> RunDiscoveryAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var count = await TagNormalization.ApplyToLibraryAsync(db, ct);
        await db.SaveChangesAsync(ct);
        clipActors?.Signal();
        return count;
    }
}
