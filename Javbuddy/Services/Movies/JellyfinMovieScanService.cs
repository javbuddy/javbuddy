using Javbuddy.Services.Jellyfin;
using Javbuddy.Services.Monitoring;

namespace Javbuddy.Services.Movies;

public interface IJellyfinMovieScanService
{
    Task ScanAsync(int movieId, string code, CancellationToken ct = default);
}

/// <summary>Movie Detail's Jellyfin refresh: report progress, look up the first match, and
/// apply ownership through MovieService. Results appear in the shared sidebar.</summary>
public sealed class JellyfinMovieScanService(
    IJellyfinClient jellyfinClient,
    IMovieService movieService,
    TaskActivityTracker activities,
    ILogger<JellyfinMovieScanService> logger) : IJellyfinMovieScanService
{
    public async Task ScanAsync(int movieId, string code, CancellationToken ct = default)
    {
        var activityId = activities.Start($"Jellyfin · {code}", "Checking Jellyfin…");
        bool isCompleted = false;
        try
        {
            if (!await jellyfinClient.IsEnabledAsync(ct))
            {
                activities.Complete(activityId, "Jellyfin integration is disabled.", failed: true);
                isCompleted = true;
                return;
            }

            var libraries = await jellyfinClient.GetSelectedLibraryNamesAsync(ct);
            if (libraries.Count == 0)
            {
                activities.Complete(activityId, "No Jellyfin libraries selected — pick at least one on the Connections page first.", failed: true);
                isCompleted = true;
                return;
            }

            var result = await jellyfinClient.LookupInSelectedLibrariesAsync(code, ct);
            if (!result.Success || result.Items is null)
            {
                activities.Complete(activityId, result.ErrorMessage ?? "Lookup failed.", failed: true);
                isCompleted = true;
                return;
            }

            // Preserve the detail page's existing first-result rule and blank-ID guard.
            var match = result.Items.FirstOrDefault();
            if (match is null || string.IsNullOrWhiteSpace(match.Id))
            {
                activities.Complete(activityId, "Not found in Jellyfin.");
                isCompleted = true;
                return;
            }

            await movieService.LinkJellyfinItemAsync(movieId, match, ct);
            activities.Complete(activityId, "Linked in Jellyfin.");
            isCompleted = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            activities.Complete(activityId, "Scan cancelled.");
            isCompleted = true;
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Jellyfin scan failed for {MovieCode}.", code);
            activities.Complete(activityId, "Jellyfin scan failed. Check the server logs for details.", failed: true);
            isCompleted = true;
        }
        finally
        {
            if (!isCompleted)
            {
                activities.Complete(activityId, "Jellyfin scan interrupted or failed.", failed: true);
            }
        }
    }
}
