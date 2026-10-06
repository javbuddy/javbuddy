using Javbuddy.Services.Ffmpeg;

namespace Javbuddy.Services.VideoRepair;

public interface IVideoRepairJobTracker
{
    VideoRepairJob? Get(int movieId);

    bool IsRunning(int movieId);

    IReadOnlyList<VideoRepairJob> GetActiveJobs();

    VideoRepairJob GetOrStart(
        int movieId,
        int? movieFileId,
        string movieCode,
        string fileName,
        Func<IProgress<FfmpegProgress>, CancellationToken, Task<VideoRepairResult>> runRepair,
        VideoFileJobKind kind = VideoFileJobKind.Repair);

    void Cancel(int movieId);
}
