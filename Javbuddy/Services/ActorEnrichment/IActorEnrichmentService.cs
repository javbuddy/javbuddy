using Javbuddy.Services.Tasks;

namespace Javbuddy.Services.ActorEnrichment;

public interface IActorEnrichmentService
{
    IReadOnlyList<string> GetAvailableSources();

    Task<ActorEnrichmentResult> EnrichActorAsync(int actorId, ActorEnrichmentOptions? options = null, CancellationToken ct = default);

    Task<ActorBatchEnrichmentResult> EnrichAllAsync(ActorEnrichmentOptions? options = null, IProgress<TaskProgress>? progress = null, CancellationToken ct = default);
}
