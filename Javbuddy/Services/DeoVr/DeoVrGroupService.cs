using System.Text.Json;
using System.Text.Json.Serialization;
using Javbuddy.Components.Shared;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.DeoVr;

/// <summary>A DeoVR group as the Settings page and the /deovr list see it: its stored filter
/// already deserialized.</summary>
public sealed record DeoVrGroupDefinition(int Id, string Name, MovieGridFilter Filter, MovieGridSort Sort);

/// <summary>A group's MovieGridFilter stored as JSON, enums by name so reordering an enum can't
/// silently change a saved group. Unreadable JSON reads as no filter.</summary>
public static class DeoVrGroupFilter
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new JsonStringEnumConverter() } };

    public static string Serialize(MovieGridFilter filter) => JsonSerializer.Serialize(filter, Options);

    public static MovieGridFilter Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new MovieGridFilter();
        try
        {
            return JsonSerializer.Deserialize<MovieGridFilter>(json, Options) ?? new MovieGridFilter();
        }
        catch (JsonException)
        {
            return new MovieGridFilter();
        }
    }
}

public interface IDeoVrGroupService
{
    /// <summary>Every group, in DeoVR order.</summary>
    Task<IReadOnlyList<DeoVrGroupDefinition>> ListAsync(CancellationToken ct = default);

    /// <summary>Adds a group at the end (id 0) or updates an existing one; returns its id.</summary>
    Task<int> SaveAsync(int id, string name, MovieGridFilter filter, string sortField, bool sortDescending, CancellationToken ct = default);

    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>Moves a group one place up (-1) or down (+1); does nothing at either end.</summary>
    Task MoveAsync(int id, int direction, CancellationToken ct = default);
}

public sealed class DeoVrGroupService(IDbContextFactory<AppDbContext> dbFactory) : IDeoVrGroupService
{
    public async Task<IReadOnlyList<DeoVrGroupDefinition>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var groups = await db.DeoVrGroups.AsNoTracking().OrderBy(g => g.Position).ThenBy(g => g.Id).ToListAsync(ct);
        return groups
            .Select(g => new DeoVrGroupDefinition(g.Id, g.Name, DeoVrGroupFilter.Deserialize(g.FilterJson),
                new MovieGridSort(NormalizeSortField(g.SortField), g.SortDescending, 0)))
            .ToList();
    }

    public async Task<int> SaveAsync(int id, string name, MovieGridFilter filter, string sortField, bool sortDescending, CancellationToken ct = default)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) throw new ArgumentException("A group needs a name.", nameof(name));
        if (trimmed.Length > 100) throw new ArgumentException("A group name can be at most 100 characters.", nameof(name));

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var group = id == 0 ? null : await db.DeoVrGroups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null)
        {
            var last = await db.DeoVrGroups.MaxAsync(g => (int?)g.Position, ct);
            group = new DeoVrGroup { Position = (last ?? -1) + 1 };
            db.DeoVrGroups.Add(group);
        }

        group.Name = trimmed;
        group.FilterJson = DeoVrGroupFilter.Serialize(filter);
        group.SortField = NormalizeSortField(sortField);
        group.SortDescending = sortDescending;
        await db.SaveChangesAsync(ct);
        return group.Id;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.DeoVrGroups.Where(g => g.Id == id).ExecuteDeleteAsync(ct);
    }

    public async Task MoveAsync(int id, int direction, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var groups = await db.DeoVrGroups.OrderBy(g => g.Position).ThenBy(g => g.Id).ToListAsync(ct);
        var index = groups.FindIndex(g => g.Id == id);
        var target = index + Math.Sign(direction);
        if (index < 0 || target < 0 || target >= groups.Count) return;

        (groups[index], groups[target]) = (groups[target], groups[index]);
        for (var i = 0; i < groups.Count; i++) groups[i].Position = i;
        await db.SaveChangesAsync(ct);
    }

    private static string NormalizeSortField(string? field) =>
        MovieFilterOptions.SortFields.Contains(field) ? field! : MovieFilterOptions.SortFields[0];
}
