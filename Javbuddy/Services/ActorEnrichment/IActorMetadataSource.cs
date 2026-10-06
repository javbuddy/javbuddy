namespace Javbuddy.Services.ActorEnrichment;

public interface IActorMetadataSource
{
    string SourceName { get; }

    /// <summary>Order of execution when multiple sources are queried. Lower values take higher precedence.</summary>
    int Priority { get; }

    /// <summary>Whether this source should be evaluated during automatic / unspecified source enrichment runs.</summary>
    bool CanAutoEnrich { get; }

    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    Task<ActorMetadataResult?> LookupAsync(ActorEnrichmentContext context, CancellationToken ct = default);
}
