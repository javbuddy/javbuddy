# Memory Diagnostics Reusable Code Templates

This reference file contains complete, production-ready classes for embedding memory testing and automated crash dump generation into any .NET application.

## 1. `DiagnosticLogger.cs`

```csharp
using System.Globalization;
using System.Text;

namespace Diagnostics;

public class DiagnosticLogger : IDisposable, IAsyncDisposable
{
    private readonly object lockObj = new();
    private readonly StreamWriter logWriter;
    private readonly StreamWriter csvWriter;

    public string LogFilePath { get; }
    public string CsvFilePath { get; }

    public DiagnosticLogger(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        LogFilePath = Path.Combine(logDirectory, $"memory_diagnostic_{timestamp}.log");
        CsvFilePath = Path.Combine(logDirectory, $"memory_metrics_{timestamp}.csv");

        var logFs = new FileStream(LogFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        logWriter = new StreamWriter(logFs, Encoding.UTF8) { AutoFlush = true };

        var csvFs = new FileStream(CsvFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        csvWriter = new StreamWriter(csvFs, Encoding.UTF8) { AutoFlush = true };

        csvWriter.WriteLine("Timestamp,Phase,ItemIndex,TotalItems,Identifier,Operation,WorkingSetMB,PrivateMemoryMB,ManagedHeapMB,Gen0,Gen1,Gen2,DurationMs");
    }

    public void Info(string message) => LogWithColor(ConsoleColor.Gray, "[INFO]", message);
    public void Success(string message) => LogWithColor(ConsoleColor.Green, "[SUCCESS]", message);
    public void Warn(string message) => LogWithColor(ConsoleColor.Yellow, "[WARN]", message);
    public void Error(string message) => LogWithColor(ConsoleColor.Red, "[ERROR]", message);

    public void Section(string title)
    {
        var line = new string('=', Math.Max(60, title.Length + 4));
        lock (lockObj)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n{line}\n  {title}\n{line}");
            Console.ResetColor();

            logWriter.WriteLine($"\n{line}\n  {title}\n{line}");
        }
    }

    public void LogMetric(string phase, int itemIndex, int totalItems, string identifier, string operation, double wsMb, double privMb, double heapMb, int gen0, int gen1, int gen2, long durationMs)
    {
        var timeStr = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        var csvLine = $"{timeStr},{EscapeCsv(phase)},{itemIndex},{totalItems},{EscapeCsv(identifier)},{EscapeCsv(operation)},{wsMb:F2},{privMb:F2},{heapMb:F2},{gen0},{gen1},{gen2},{durationMs}";
        lock (lockObj) csvWriter.WriteLine(csvLine);
    }

    private void LogWithColor(ConsoleColor color, string tag, string message)
    {
        var formatted = $"[{DateTime.Now:HH:mm:ss.fff}] {tag} {message}";
        lock (lockObj)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(formatted);
            Console.ResetColor();
            logWriter.WriteLine(formatted);
        }
    }

    private static string EscapeCsv(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    public void Dispose()
    {
        lock (lockObj)
        {
            logWriter.Flush(); logWriter.Dispose();
            csvWriter.Flush(); csvWriter.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await logWriter.FlushAsync(); await logWriter.DisposeAsync();
        await csvWriter.FlushAsync(); await csvWriter.DisposeAsync();
    }
}
```

## 2. `MemoryMonitor.cs`

```csharp
using System.Diagnostics;

namespace Diagnostics;

public record MemorySnapshot(double WorkingSetMb, double PrivateMemoryMb, double ManagedHeapMb, int Gen0, int Gen1, int Gen2, DateTime Timestamp);

public class MemoryMonitor : IDisposable, IAsyncDisposable
{
    private readonly DiagnosticLogger logger;
    private readonly string dumpDirectory;
    private readonly double thresholdMb;
    private readonly CancellationTokenSource cts = new();
    private readonly Task monitorTask;
    private double nextDumpThresholdMb;
    private const double StepMb = 1024.0;

    public MemorySnapshot Initial { get; }
    public MemorySnapshot Peak { get; private set; }

    public MemoryMonitor(DiagnosticLogger logger, string dumpDirectory, double thresholdGb = 2.0, int samplingIntervalMs = 500)
    {
        this.logger = logger;
        this.dumpDirectory = dumpDirectory;
        this.thresholdMb = thresholdGb * 1024.0;
        this.nextDumpThresholdMb = this.thresholdMb;

        Initial = Sample();
        Peak = Initial;
        monitorTask = Task.Run(() => Loop(samplingIntervalMs, cts.Token));
    }

    public MemorySnapshot Sample()
    {
        using var process = Process.GetCurrentProcess();
        var wsMb = process.WorkingSet64 / (1024.0 * 1024.0);
        var privMb = process.PrivateMemorySize64 / (1024.0 * 1024.0);
        var heapMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
        var snap = new MemorySnapshot(wsMb, privMb, heapMb, GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), DateTime.UtcNow);

        if (Peak is null || snap.WorkingSetMb > Peak.WorkingSetMb) Peak = snap;
        return snap;
    }

    private async Task Loop(int intervalMs, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var snap = Sample();
                var maxUsedMb = Math.Max(snap.WorkingSetMb, snap.PrivateMemoryMb);
                if (maxUsedMb >= nextDumpThresholdMb)
                {
                    var trigger = nextDumpThresholdMb;
                    nextDumpThresholdMb += StepMb;
                    logger.Warn($"RAM USAGE REACHED {maxUsedMb:F2} MB (Threshold: {trigger:F0} MB)! Capturing full dump...");
                    TriggerDump($"threshold_{trigger:F0}MB_breached", snap);
                }
                await Task.Delay(intervalMs, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { logger.Warn($"Monitor error: {ex.Message}"); }
        }
    }

    public string? TriggerDump(string tag, MemorySnapshot? snap = null)
    {
        snap ??= Sample();
        var dumpPath = Path.Combine(dumpDirectory, $"dump_{tag}_{DateTime.Now:yyyyMMdd_HHmmss}_{snap.WorkingSetMb:F0}MB_ws.dmp");
        var (success, error, sizeBytes) = MemoryDumpWriter.CaptureFullDump(dumpPath);
        if (success)
        {
            logger.Success($"Memory dump saved: {dumpPath} ({sizeBytes / (1024.0 * 1024.0):F2} MB)");
            return dumpPath;
        }
        logger.Error($"Failed dump: {error}");
        return null;
    }

    public void Dispose()
    {
        try { cts.Cancel(); monitorTask.Wait(TimeSpan.FromSeconds(2)); cts.Dispose(); } catch { }
    }

    public async ValueTask DisposeAsync()
    {
        try { cts.Cancel(); await monitorTask.WaitAsync(TimeSpan.FromSeconds(2)); cts.Dispose(); } catch { }
    }
}
```
