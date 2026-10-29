using System.Diagnostics;
using System.Globalization;
using VarjoEye.Core.Contracts;
using VarjoEye.Core.Models;

namespace VarjoEye.Core.Services;

/// <summary>Five-second throughput and mean capture-to-publication age (not filter phase lag).</summary>
public sealed class TrackingDiagnosticsService : IDisposable
{
    private readonly ITrackingDataProvider _tracking;
    private readonly IBaballoniaTrackingService _baballonia;
    private readonly ILogService _logs;
    private readonly IAppSettingsService _settings;
    private readonly List<(CancellationTokenSource Run, Task Loop)> _retired = [];
    private bool _started;
    private volatile bool _enabled;
    private readonly object _gate = new();
    private readonly Counter[] _counters = [new(), new(), new()];
    private CancellationTokenSource? _run;
    private Task? _loop;
    private long _clockNative;
    private long _clockStopwatch;
    private long _lastReport;
    private bool _disposed;

    public TrackingDiagnosticsService(ITrackingDataProvider tracking, IBaballoniaTrackingService baballonia, ILogService logs, IAppSettingsService settings)
    {
        _tracking = tracking;
        _baballonia = baballonia;
        _logs = logs;
        _settings = settings;
    }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) return;
            _started = true;
            _settings.Changed += OnSettingsChanged;
            _tracking.GazeReceived += OnGaze;
            _tracking.CameraFrameReceived += OnCamera;
            _baballonia.Updated += OnBaballonia;
            ApplySettingsLocked();
        }
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (!_disposed && _started) ApplySettingsLocked();
        }
    }

    private void ApplySettingsLocked()
    {
        var enabled = _settings.Current.TrackingDiagnosticsEnabled;
        if (_enabled == enabled) return;
        _enabled = enabled;
        if (_run is not null)
        {
            _run.Cancel();
            _retired.Add((_run, _loop!));
            _run = null;
            _loop = null;
        }
        for (var i = _retired.Count - 1; i >= 0; i--)
        {
            if (!_retired[i].Loop.IsCompleted) continue;
            _retired[i].Run.Dispose();
            _retired.RemoveAt(i);
        }
        foreach (var counter in _counters) counter.Reset();
        _clockNative = 0;
        _lastReport = Stopwatch.GetTimestamp();
        if (enabled)
        {
            _run = new CancellationTokenSource();
            var token = _run.Token;
            _loop = Task.Run(() => ReportLoopAsync(token));
        }
    }

    private void RefreshClock()
    {
        // Never call Varjo from its camera callback: shutdown may hold the runtime lock.
        var before = Stopwatch.GetTimestamp();
        var native = _tracking.GetCurrentTimeNanoseconds();
        var after = Stopwatch.GetTimestamp();
        lock (_gate)
        {
            _clockNative = native;
            _clockStopwatch = before + (after - before) / 2;
        }
    }

    private void OnGaze(object? sender, GazeSample sample)
    {
        if (!_enabled) return;
        // Start may run before the native session opens. Initialize on the first gaze event.
        if (Volatile.Read(ref _clockNative) <= 0) RefreshClock();
        Record(0, sample.FrameNumber, sample.CaptureTimeNanoseconds);
    }
    private void OnCamera(object? sender, EyeCameraFrame frame) => Record(2, frame.FrameNumber, frame.CaptureTimeNanoseconds);
    private void OnBaballonia(object? sender, BaballoniaTrackingSample sample)
    {
        if (sample.HasResult && sample.Result is { } result)
            Record(1, result.FrameNumber, result.CaptureTimeNanoseconds);
    }

    private void Record(int source, long frame, long capture)
    {
        if (!_enabled) return;
        var ticks = Stopwatch.GetTimestamp();
        lock (_gate)
        {
            if (_disposed || !_enabled) return;
            var now = _clockNative > 0
                ? _clockNative + (long)((ticks - _clockStopwatch) * (1_000_000_000d / Stopwatch.Frequency))
                : 0;
            _counters[source].Add(frame, capture, now);
        }
    }

    private async Task ReportLoopAsync(CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            RefreshClock();
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                RefreshClock();
                var now = Stopwatch.GetTimestamp();
                (string Latency, string Fps) varjo, babble, camera;
                lock (_gate)
                {
                    if (!_enabled || token.IsCancellationRequested) continue;
                    var seconds = Stopwatch.GetElapsedTime(_lastReport, now).TotalSeconds;
                    _lastReport = now;
                    varjo = _counters[0].Take(seconds);
                    babble = _counters[1].Take(seconds);
                    camera = _counters[2].Take(seconds);
                }
                token.ThrowIfCancellationRequested();
                // File I/O and log subscribers must never hold the acquisition counters' lock.
                _logs.Add(LogLevel.Information,
                    $"[Latency] Varjo API: {varjo.Latency} | Baballonia: {babble.Latency} | Stream Camera: {camera.Latency}");
                token.ThrowIfCancellationRequested();
                _logs.Add(LogLevel.Information,
                    $"[FPS] Varjo API: {varjo.Fps} | Baballonia: {babble.Fps} | Stream Camera: {camera.Fps}");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        CancellationTokenSource? run;
        Task? loop;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _enabled = false;
            run = _run;
            loop = _loop;
        }
        _settings.Changed -= OnSettingsChanged;
        _tracking.GazeReceived -= OnGaze;
        _tracking.CameraFrameReceived -= OnCamera;
        _baballonia.Updated -= OnBaballonia;
        run?.Cancel();
        loop?.GetAwaiter().GetResult();
        run?.Dispose();
        foreach (var retired in _retired)
        {
            retired.Loop.GetAwaiter().GetResult();
            retired.Run.Dispose();
        }
        _retired.Clear();
    }

    internal sealed class Counter
    {
        private long? _lastFrame;
        private long _count;
        private long _latencyCount;
        private double _latencySum;

        public void Reset()
        {
            _lastFrame = null;
            _count = _latencyCount = 0;
            _latencySum = 0;
        }

        public void Add(long frame, long capture, long now)
        {
            // Left/right camera callbacks represent one stereo frame; duplicate updates aren't FPS.
            if (frame == _lastFrame) return;
            _lastFrame = frame;
            _count++;
            if (capture > 0 && now >= capture)
            {
                _latencySum += (now - capture) / 1_000_000d;
                _latencyCount++;
            }
        }

        public (string Latency, string Fps) Take(double seconds)
        {
            var latency = _latencyCount == 0 ? "N/A" :
                (_latencySum / _latencyCount).ToString("0.0", CultureInfo.InvariantCulture) + " ms";
            var fps = (seconds > 0 ? _count / seconds : 0).ToString("0.0", CultureInfo.InvariantCulture);
            _count = _latencyCount = 0;
            _latencySum = 0;
            return (latency, fps);
        }
    }
}
