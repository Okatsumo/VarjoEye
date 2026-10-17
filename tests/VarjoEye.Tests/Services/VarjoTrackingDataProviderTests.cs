using VarjoEye.Core.Models;
using VarjoEye.Core.Services;
using VarjoEye.Tests.Fakes;
using Xunit;

namespace VarjoEye.Tests.Services;

public sealed class VarjoTrackingDataProviderTests
{
    [Fact]
    public async Task FailedConnect_RetriesUntilRuntimeIsAvailable()
    {
        var runtime = new FakeVarjoRuntime();
        runtime.EnqueueConnectResult(OperationResult.Failure("Varjo runtime is unavailable."));
        runtime.EnqueueConnectResult(OperationResult.Failure("Varjo runtime is unavailable."));
        using var provider = Create(runtime);

        provider.Start();
        await WaitUntil(() => runtime.ConnectCount >= 3 && runtime.InitializeCount >= 1);

        Assert.True(runtime.IsSessionOpen);
        Assert.True(provider.Current.HasTrackingData);
        await provider.StopAsync();
    }

    [Fact]
    public async Task StopAsync_WaitsForPollThenDisconnectsIdleSession()
    {
        var runtime = new FakeVarjoRuntime();
        using var provider = Create(runtime);
        provider.Start();
        await WaitUntil(() => runtime.InitializeCount >= 1);

        await provider.StopAsync();

        Assert.Equal(1, runtime.DisconnectCount);
        Assert.False(runtime.DisconnectedWhileBusy);
        Assert.False(runtime.IsSessionOpen);
        Assert.False(provider.Current.HasTrackingData);
        Assert.Equal(ConnectionStatus.Disconnected, provider.Current.Headset.Connection);
    }

    [Fact]
    public async Task HeadsetReconnect_ReinitializesGaze()
    {
        var runtime = new FakeVarjoRuntime();
        using var provider = Create(runtime);
        provider.Start();
        await WaitUntil(() => runtime.InitializeCount >= 1);

        runtime.HeadsetConnected = false;
        await WaitUntil(() => !runtime.IsEyeTrackingInitialized);

        runtime.HeadsetConnected = true;
        await WaitUntil(() => runtime.InitializeCount >= 2 && runtime.IsEyeTrackingInitialized);

        Assert.True(provider.Current.HasTrackingData);
        await provider.StopAsync();
    }

    [Fact]
    public async Task HeadsetConnected_StartsCameraStreamAtTrackingFrequency()
    {
        var runtime = new FakeVarjoRuntime();
        using var provider = Create(runtime);
        provider.Start();
        await WaitUntil(() => runtime.StartCameraCount >= 1 && runtime.IsEyeCameraStreaming);

        Assert.Equal(EyeTrackingFrequency.Hz200, runtime.LastCameraFrequency);
        await provider.StopAsync();
        Assert.True(runtime.StopCameraCount >= 1);
        Assert.False(runtime.IsEyeCameraStreaming);
    }

    [Fact]
    public async Task HeadsetDisconnect_StopsCameraStream()
    {
        var runtime = new FakeVarjoRuntime();
        using var provider = Create(runtime);
        provider.Start();
        await WaitUntil(() => runtime.IsEyeCameraStreaming);

        runtime.HeadsetConnected = false;
        await WaitUntil(() => !runtime.IsEyeCameraStreaming);
        await provider.StopAsync();
    }

    private static VarjoTrackingDataProvider Create(FakeVarjoRuntime runtime) =>
        new(
            runtime,
            new InMemoryAppSettingsService(),
            new InMemoryLogService(),
            TimeSpan.FromMilliseconds(40),
            TimeSpan.FromSeconds(2));

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(15);
        }

        throw new TimeoutException("Condition was not met in time.");
    }
}
