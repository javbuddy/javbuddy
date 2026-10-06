namespace Javbuddy.Services.Infrastructure;

/// <summary>Application-owned execution for user-triggered work that must outlive the click
/// handler (and the page) that started it. Each job gets its own DI scope for exactly its own
/// lifetime, has its exceptions observed and logged here, and receives a token that fires on
/// application shutdown — <see cref="StopAsync"/> cancels it and waits (bounded by the host's
/// shutdown timeout) for in-flight jobs to finish. Callers pass a self-contained delegate over
/// immutable inputs, so a job never retains the component that queued it.</summary>
public sealed class BackgroundJobRunner(IServiceScopeFactory scopeFactory, ILogger<BackgroundJobRunner> logger) : IHostedService
{
    private readonly IServiceScopeFactory scopeFactory = scopeFactory;
    private readonly ILogger<BackgroundJobRunner> logger = logger;
    private readonly CancellationTokenSource stopping = new();
    private readonly Lock gate = new();
    private readonly HashSet<Task> running = [];

    /// <summary>Starts <paramref name="work"/> on the thread pool and returns immediately. The
    /// returned task completes when the job ends and never faults — failures are logged here —
    /// so fire-and-forget callers can safely discard it. Jobs queued after shutdown has begun
    /// are dropped.</summary>
    public Task Enqueue(string jobName, Func<IServiceProvider, CancellationToken, Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        lock (gate)
        {
            if (stopping.IsCancellationRequested)
            {
                logger.LogWarning("Background job {JobName} was not started because the application is shutting down.", jobName);
                return Task.CompletedTask;
            }

            var job = Task.Run(() => RunAsync(jobName, work, stopping.Token));
            running.Add(job);
            _ = job.ContinueWith(Forget, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            return job;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task[] inFlight;
        lock (gate)
        {
            stopping.Cancel();
            inFlight = [.. running];
        }

        try
        {
            await Task.WhenAll(inFlight).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Shutdown timed out with {Count} background job(s) still running.", inFlight.Count(t => !t.IsCompleted));
        }
    }

    private async Task RunAsync(string jobName, Func<IServiceProvider, CancellationToken, Task> work, CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await work(scope.ServiceProvider, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogInformation("Background job {JobName} was cancelled by application shutdown.", jobName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Background job {JobName} failed.", jobName);
        }
    }

    private void Forget(Task job)
    {
        lock (gate)
        {
            running.Remove(job);
        }
    }
}
