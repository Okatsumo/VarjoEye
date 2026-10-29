using VarjoEye.Core.Models;
using VarjoEye.Core.Services.Baballonia;
using Xunit;
using Xunit.Abstractions;

namespace VarjoEye.Tests.Services;

public sealed class BaballoniaInferenceAllocationTests(ITestOutputHelper output)
{
    [Fact]
    public void RepeatedInference_ReusesLargeInputBuffer()
    {
        using var inference = new BaballoniaEyeInference(TestModelPaths.Create().StockPath, InferenceDevice.Cpu);
        var pixels = Enumerable.Repeat((byte)100, 64).ToArray();
        var window = new List<CameraEyeFramePair>();
        BaballoniaRawOutput? expected = null;
        for (var i = 1; i <= 10; i++) Run(i);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var gen2 = GC.CollectionCount(2);
        for (var i = 11; i <= 110; i++) Run(i);
        var perResult = (GC.GetAllocatedBytesForCurrentThread() - before) / 100;
        output.WriteLine($"Managed bytes/result: {perResult}; Gen2 collections: {GC.CollectionCount(2) - gen2}");
        // A recreated 512 KiB tensor exceeded 700 KiB/result on this fixture.
        Assert.True(perResult < 400_000, $"Unexpected allocations per result: {perResult}");

        void Run(int number)
        {
            var timestamp = number * 5_000_000L;
            window.Add(new(number, DateTimeOffset.UtcNow,
                new(true, 8, 8, pixels, number, timestamp), new(false, 8, 8, pixels, number, timestamp)));
            if (window.Count > 4) window.RemoveAt(0);
            var result = inference.Run(window, false);
            if (window.Count == 4)
            {
                Assert.NotNull(result);
                expected ??= result.RawOutput;
                Assert.Equal(expected, result.RawOutput);
            }
        }
    }
}
