namespace Javbuddy.Services.MovieDiscovery;

/// <summary>One studio's direct site, scraped for new/upcoming releases. Pure fetcher — no DB
/// knowledge, mirroring ActorEnrichment's <c>IActorMetadataSource</c> shape. Register additional
/// studios by adding another implementation — or, for a studio site built on an already-supported
/// template/vendor (see <c>UpTimelyDiscoverySource</c>), just another registration of that same
/// class with its own domain/name/logo (Program.cs); <see cref="IMovieDiscoveryService"/>
/// aggregates whatever's registered.</summary>
public interface IStudioDiscoverySource
{
    /// <summary>Stable identifier stored on each candidate as <c>SourceName</c> and shown in the UI.</summary>
    string SourceName { get; }

    /// <summary>A small bundled logo asset (e.g. <c>/studio-logos/s1.png</c>) shown on that studio's
    /// section header on Movies &gt; Discover. Bundled rather than hot-linked from the studio's own
    /// site, so it doesn't break if their site changes. Null shows no logo.</summary>
    StudioLogo? Logo { get; }

    Task<IReadOnlyList<DiscoveredMovieItem>> ScanAsync(CancellationToken ct = default);
}
