using Javbuddy.Services.Images;

namespace Javbuddy.Tests.Services.Images;

public class ImageConversionGateTests
{
    [Fact]
    public async Task RunAsync_NeverRunsMoreThanMaxConcurrencyAtOnce()
    {
        var running = 0;
        var maxObserved = 0;

        int Convert()
        {
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref maxObserved, now);
            Thread.Sleep(20);
            Interlocked.Decrement(ref running);
            return now;
        }

        var tasks = Enumerable.Range(0, ImageConversionGate.MaxConcurrency * 4)
            .Select(_ => Task.Run(() => ImageConversionGate.RunAsync(Convert)))
            .ToList();
        await Task.WhenAll(tasks);

        // The gate is process-wide, so other tests converting at the same time can only lower
        // what this test observes, never raise it.
        Assert.InRange(maxObserved, 1, ImageConversionGate.MaxConcurrency);
    }

    [Fact]
    public async Task RunAsync_ReleasesTheSlot_WhenConvertThrows()
    {
        for (var i = 0; i < ImageConversionGate.MaxConcurrency + 1; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => ImageConversionGate.RunAsync<int>(() => throw new InvalidOperationException()));
        }

        Assert.Equal(1, await ImageConversionGate.RunAsync(() => 1).WaitAsync(TimeSpan.FromSeconds(10)));
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)))
        {
            if (Interlocked.CompareExchange(ref target, value, current) == current) return;
        }
    }
}
