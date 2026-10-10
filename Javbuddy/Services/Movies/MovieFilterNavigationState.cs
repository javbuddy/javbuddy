using Javbuddy.Services.R18Dev;

namespace Javbuddy.Services.Movies;

/// <summary><paramref name="CatalogSeries"/>/<paramref name="CatalogLabel"/> open the Movies page in its
/// "All releases (r18.dev)" browse, narrowed to that r18.dev series or label. <paramref name="ActorTagId"/> filters
/// the library by an actor tag (Tag.IsActorTag), which the Genre filter doesn't list.</summary>
public sealed record MovieFilterNavigationTarget(
    string? Studio = null,
    string? Genre = null,
    string? CodePrefix = null,
    DateTimeOffset? Timestamp = null,
    R18DevCatalogRef? CatalogSeries = null,
    R18DevCatalogRef? CatalogLabel = null,
    int? ActorTagId = null)
{
    public bool IsCatalogTarget => CatalogSeries is not null || CatalogLabel is not null;
}

/// <summary>Holds short-lived, user-triggered navigation filter targets (e.g. clicking a studio
/// or genre badge on MovieDetail) so the Movies grid at "/" can receive and apply the filter
/// without polluting the URL with query string parameters. Singleton so it bridges circuits and
/// prerendered requests across enhanced navigation.</summary>
public sealed class MovieFilterNavigationState(TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan Expiry = TimeSpan.FromSeconds(30);
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;
    private readonly object gate = new();
    private MovieFilterNavigationTarget? pending;

    public void SetPendingFilter(string? studio = null, string? genre = null, string? codePrefix = null)
    {
        lock (gate)
        {
            pending = new MovieFilterNavigationTarget(studio, genre, codePrefix, this.timeProvider.GetUtcNow());
        }
    }

    /// <summary>Opens the Movies page's r18.dev catalog browse narrowed to one series or label.</summary>
    public void SetPendingCatalogFilter(R18DevCatalogRef? series = null, R18DevCatalogRef? label = null)
    {
        lock (gate)
        {
            pending = new MovieFilterNavigationTarget(Timestamp: this.timeProvider.GetUtcNow(), CatalogSeries: series, CatalogLabel: label);
        }
    }

    /// <summary>Opens the Movies grid filtered by one actor tag.</summary>
    public void SetPendingActorTagFilter(int actorTagId)
    {
        lock (gate)
        {
            pending = new MovieFilterNavigationTarget(Timestamp: this.timeProvider.GetUtcNow(), ActorTagId: actorTagId);
        }
    }

    public MovieFilterNavigationTarget? PeekPendingFilter()
    {
        lock (gate)
        {
            if (pending is null) return null;
            if (pending.Timestamp.HasValue && this.timeProvider.GetUtcNow() - pending.Timestamp.Value > Expiry)
            {
                pending = null;
                return null;
            }
            return pending;
        }
    }

    public MovieFilterNavigationTarget? ConsumePendingFilter()
    {
        lock (gate)
        {
            var target = PeekPendingFilter();
            pending = null;
            return target;
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            pending = null;
        }
    }
}
