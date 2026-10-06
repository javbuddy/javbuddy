using Javbuddy.Services.Ffmpeg;

namespace Javbuddy.Services.VideoRepair;

public interface IVideoRepairService
{
    Task<IReadOnlyList<VideoRepairCandidate>> GetRepairCandidatesAsync(int movieId, CancellationToken ct = default);

    Task<VideoRepairResult> RepairAsync(
        int movieId,
        int? movieFileId = null,
        IProgress<FfmpegProgress>? progress = null,
        CancellationToken ct = default);

    Task<VideoRepairJob> StartRepairJobAsync(
        int movieId,
        int? movieFileId = null,
        CancellationToken ct = default);

    Task<int> StartBatchRepairAsync(
        IReadOnlyList<int> movieIds,
        CancellationToken ct = default);
}
