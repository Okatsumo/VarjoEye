using VarjoEye.Core.Contracts;
using VarjoEye.Core.Models;
using VarjoEye.Core.Services.Baballonia.Sampling;

namespace VarjoEye.Core.Services.Baballonia;

public sealed class BaballoniaTrainer : IBaballoniaTrainer
{
    private static readonly TimeSpan WaitingDuration = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan GazeDuration = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan BlinkDuration = TimeSpan.FromSeconds(20);
    private const int MaxSamples = 8000;

    private readonly ITrackingDataProvider _tracking;
    private readonly BaballoniaEyeModel _model;
    private readonly IBaballoniaTrackingService _baballonia;
    private readonly ICalibrationOverlay _overlay;
    private readonly IBaballoniaCalibrator _calibrator;
    private readonly BaballoniaCalibrationStore _store;
    private readonly ILogService _logs;
    private readonly object _gate = new();
    private readonly TrackingSampleSynchronizer _sync = new(
        SampleCollectionOptions.Trainer.GazeCapacity,
        SampleCollectionOptions.Trainer.MaxPairDeltaNanoseconds);
    private readonly SampleRateLimiter _rate = new(SampleCollectionOptions.Trainer.MinSampleSpacingNanoseconds);
    private readonly List<BaballoniaFeatureSample> _left = [];
    private readonly List<BaballoniaFeatureSample> _right = [];
    private string _statusText = "Last-layer training idle";
    private bool _busy;
    private CancellationToken _activeTrainingToken;
    private bool _collecting;
    private bool _blinkPhase;
    private bool _disposed;
    private int _resultsSeen;
    private int _missingFeatures;
    private CancellationTokenSource? _run;
    private Task? _loop;

    public BaballoniaTrainer(
        ITrackingDataProvider tracking,
        IBaballoniaTrackingService baballonia,
        ICalibrationOverlay overlay,
        IBaballoniaCalibrator calibrator,
        BaballoniaCalibrationStore store,
        ILogService logs,
        BaballoniaEyeModel model)
    {
        _tracking = tracking;
        _model = model;
        _baballonia = baballonia;
        _overlay = overlay;
        _calibrator = calibrator;
        _store = store;
        _logs = logs;
        _statusText = _model.HasUserModel
            ? "Using trained last layer"
            : "Last-layer training idle";
        _tracking.GazeReceived += OnGazeReceived;
        _baballonia.Updated += OnBaballoniaUpdated;
        _tracking.Updated += OnTrackingUpdated;
    }

    public string StatusText
    {
        get
        {
            lock (_gate)
            {
                return _statusText;
            }
        }
    }

    public bool IsBusy
    {
        get
        {
            lock (_gate)
            {
                return _busy;
            }
        }
    }

    public event EventHandler? Changed;

    public bool TryBegin()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_busy || _calibrator.IsCollecting)
            {
                return false;
            }

            var headset = _tracking.Current.Headset;
            if (headset.Connection != ConnectionStatus.Connected ||
                headset.EyeTrackingStatus == "Calibrating" ||
                headset.EyeTrackingStatus != "Ready")
            {
                return false;
            }

            _busy = true;
            _baballonia.SetTrainingFeaturesEnabled(true);
            _collecting = false;
            _blinkPhase = false;
            _left.Clear();
            _right.Clear();
            _sync.Clear();
            _rate.Reset();
            _resultsSeen = 0;
            _missingFeatures = 0;
            _statusText = "Training: look at the center";
            _run?.Cancel();
            _run?.Dispose();
            _run = new CancellationTokenSource();
            var token = _run.Token;
            _activeTrainingToken = token;
            _loop = Task.Run(() => RunAsync(token));
        }

        Notify();
        return true;
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            _overlay.Show(OverlayHint(CalibrationOverlayPhase.Waiting, WaitingDuration.TotalSeconds));
            await Task.Delay(WaitingDuration, token).ConfigureAwait(false);

            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _collecting = true;
                _blinkPhase = false;
                _statusText = "Training: follow the purple target (120 s)";
            }

            Notify();
            _overlay.Show(OverlayHint(CalibrationOverlayPhase.Sweep, GazeDuration.TotalSeconds));
            await Task.Delay(GazeDuration, token).ConfigureAwait(false);

            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _blinkPhase = true;
                _statusText = "Training: close both eyes (20 s)";
            }

            Notify();
            _overlay.Show(OverlayHint(CalibrationOverlayPhase.Blink, BlinkDuration.TotalSeconds));
            await Task.Delay(BlinkDuration, token).ConfigureAwait(false);

            lock (_gate)
            {
                _collecting = false;
                _baballonia.SetTrainingFeaturesEnabled(false);
                _statusText = "Training: fitting last layer…";
            }

            Notify();
            _overlay.Hide();
            FitAndSave();
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                if (_busy && _activeTrainingToken == token)
                {
                    CancelLocked("Training cancelled");
                }
            }
        }
        catch (Exception exception)
        {
            _logs.Add(LogLevel.Error, $"Last-layer training failed: {exception.Message}");
            lock (_gate)
            {
                if (_activeTrainingToken == token) CancelLocked("Training failed");
            }
        }
        finally
        {
            lock (_gate)
            {
                // A previous session's cleanup must not disable features for a newer session.
                if (_activeTrainingToken == token) _baballonia.SetTrainingFeaturesEnabled(false);
            }
            _overlay.Hide();
            Notify();
        }
    }

    private void FitAndSave()
    {
        BaballoniaFeatureSample[] left;
        BaballoniaFeatureSample[] right;
        lock (_gate)
        {
            left = [.. _left];
            right = [.. _right];
            _logs.Add(LogLevel.Information,
                $"Last-layer samples: model={_resultsSeen}, missing features={_missingFeatures}, left={left.Length}, right={right.Length}.");
        }

        var stockBytes = File.ReadAllBytes(_model.StockPath);
        var stock = BaballoniaOnnxGraph.ReadHead(stockBytes);
        var fitted = BaballoniaLastLayerFitter.Fit(left, right, stock, out var summary);
        if (fitted is null)
        {
            lock (_gate)
            {
                _busy = false;
                _statusText = $"Training rejected ({summary})";
            }

            _logs.Add(LogLevel.Warning, $"Last-layer fit rejected: {summary}.");
            return;
        }

        Directory.CreateDirectory(_model.UserDirectory);
        var patched = BaballoniaOnnxGraph.WriteHead(stockBytes, fitted.Value);
        var temporary = _model.UserPath + ".tmp";
        File.WriteAllBytes(temporary, patched);
        File.Move(temporary, _model.UserPath, overwrite: true);
        _store.Current = null;
        _baballonia.ReloadModel();

        lock (_gate)
        {
            _busy = false;
            _left.Clear();
            _right.Clear();
            _statusText = $"Trained last layer ({summary}). Recalibrate for affine map.";
        }

        _logs.Add(LogLevel.Information, $"Last-layer training saved to {_model.UserPath} ({summary}).");
    }

    private void CancelLocked(string status)
    {
        _baballonia.SetTrainingFeaturesEnabled(false);
        _collecting = false;
        _busy = false;
        _left.Clear();
        _right.Clear();
        _statusText = status;
        try
        {
            _run?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void OnGazeReceived(object? sender, GazeSample sample)
    {
        lock (_gate)
        {
            if (_disposed || !_collecting)
            {
                return;
            }

            _sync.Push(sample);
        }
    }

    private void OnBaballoniaUpdated(object? sender, BaballoniaTrackingSample sample)
    {
        if (sample.Result is not { } result)
        {
            return;
        }

        var notify = false;
        lock (_gate)
        {
            if (_disposed || !_collecting)
            {
                return;
            }

            _resultsSeen++;
            if (TrackingSampleValidator.Features(
                    result.LeftFeatures, result.RightFeatures, BaballoniaOnnxGraph.FeatureSize)
                != SampleRejectionReason.None)
            {
                _missingFeatures++;
                return;
            }

            if (TrackingSampleValidator.Timestamp(result.CaptureTimeNanoseconds) != SampleRejectionReason.None)
            {
                return;
            }

            var gaze = _sync.FindNearest(result.CaptureTimeNanoseconds);
            if (gaze is null && !_blinkPhase)
            {
                return;
            }

            TryAdd(_left, result.LeftFeatures!, gaze?.Left, gaze, result.CaptureTimeNanoseconds, gaze?.LeftStatus ?? 0, true);
            TryAdd(_right, result.RightFeatures!, gaze?.Right, gaze, result.CaptureTimeNanoseconds, gaze?.RightStatus ?? 0, false);
            var count = Math.Min(_left.Count, _right.Count);
            if (count > 0 && count % 100 == 0)
            {
                _statusText = _blinkPhase
                    ? $"Training: eyes closed ({count} pairs)"
                    : $"Training: follow the target ({count} pairs)";
                notify = true;
            }
        }

        if (notify)
        {
            Notify();
        }
    }

    private void TryAdd(
        List<BaballoniaFeatureSample> samples,
        float[] features,
        EyeTrackingEye? eye,
        GazeSample? frame,
        long captureTime,
        int eyeStatus,
        bool left)
    {
        if (samples.Count >= MaxSamples)
        {
            return;
        }

        if (!_rate.WouldAccept(left, captureTime))
        {
            return;
        }

        var gazeValid = !_blinkPhase && frame is not null &&
            TrackingSampleValidator.GazeFrame(frame.Status, frame.Stability) == SampleRejectionReason.None &&
            eye is { } visible &&
            TrackingSampleValidator.IsTrackedEye(eyeStatus) &&
            double.IsFinite(visible.GazeDirection.X) && double.IsFinite(visible.GazeDirection.Y);
        if (!_blinkPhase && !gazeValid)
        {
            return;
        }

        if (!BlinkLabelPolicy.TryLabel(_blinkPhase, eye, out var lid))
        {
            return;
        }

        var targetX = gazeValid
            ? BaballoniaEyeExpression.ToOnnxAxis(Math.Clamp(eye!.GazeDirection.X, -1, 1))
            : 0.5;
        var targetY = gazeValid
            ? BaballoniaEyeExpression.ToOnnxAxis(Math.Clamp(-eye!.GazeDirection.Y, -1, 1))
            : 0.5;
        var copy = new float[BaballoniaOnnxGraph.FeatureSize];
        features.AsSpan().CopyTo(copy);
        samples.Add(new BaballoniaFeatureSample(copy, targetY, targetX, lid, gazeValid, true));
        _rate.Mark(left, captureTime);
    }

    internal static bool TryLidTarget(bool blinkPhase, EyeTrackingEye? eye, out double lid) =>
        BlinkLabelPolicy.TryLabel(blinkPhase, eye, out lid);

    private CalibrationOverlayHint OverlayHint(CalibrationOverlayPhase phase, double durationSeconds) =>
        new(phase, 0, 0, 0, durationSeconds, _tracking.GetCurrentTimeNanoseconds());

    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        Task? loop;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _baballonia.SetTrainingFeaturesEnabled(false);
            _collecting = false;
            _busy = false;
            loop = _loop;
            try
            {
                _run?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        try
        {
            loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _tracking.GazeReceived -= OnGazeReceived;
        _baballonia.Updated -= OnBaballoniaUpdated;
        _tracking.Updated -= OnTrackingUpdated;
        _run?.Dispose();
        _overlay.Hide();
    }

    private void OnTrackingUpdated(object? sender, EyeTrackingState state)
    {
        lock (_gate)
        {
            if (_disposed || !_busy)
            {
                return;
            }

            if (state.Headset.Connection != ConnectionStatus.Connected ||
                state.Headset.EyeTrackingStatus == "Calibrating")
            {
                CancelLocked("Training cancelled");
            }
            else
            {
                return;
            }
        }

        Notify();
    }
}
