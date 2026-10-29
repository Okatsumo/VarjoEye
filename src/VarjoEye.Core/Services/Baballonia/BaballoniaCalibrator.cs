using VarjoEye.Core.Contracts;
using VarjoEye.Core.Models;
using VarjoEye.Core.Services.Baballonia.Sampling;

namespace VarjoEye.Core.Services.Baballonia;

public sealed class BaballoniaCalibrator : IBaballoniaCalibrator
{
    private const int MaxSamplesPerEye = 4000;
    private const long CollectionTimeoutNanoseconds = 120_000_000_000;
    private const long VarjoSettlingDelayNanoseconds = 7_000_000_000;
    private const long PostCalibrationWindowNanoseconds = 15_000_000_000;
    private static readonly TimeSpan VarjoSettlingDelay = TimeSpan.FromSeconds(7);
    private static readonly TimeSpan PostCalibrationWindow = TimeSpan.FromSeconds(15);

    private readonly ITrackingDataProvider _tracking;
    private readonly IBaballoniaTrackingService _baballonia;
    private readonly ICalibrationOverlay _overlay;
    private readonly BaballoniaCalibrationStore _store;
    private readonly ILogService _logs;
    private readonly object _gate = new();
    private readonly TrackingSampleSynchronizer _sync = new(
        SampleCollectionOptions.Calibrator.GazeCapacity,
        SampleCollectionOptions.Calibrator.MaxPairDeltaNanoseconds);
    private readonly Queue<BaballoniaEyeResult> _pendingResults = new();
    private readonly List<BaballoniaCalibrationSample> _left = [];
    private readonly List<BaballoniaCalibrationSample> _right = [];
    private readonly List<BaballoniaHeadSample> _heads = [];
    private string _statusText = "Waiting for Varjo calibration";
    private bool _collecting;
    private bool _sawCalibrating;
    private long? _startedAtNanoseconds;
    private long? _nativeCalibrationCompletedAtNanoseconds;
    private long? _babbleCollectionStartedAtNanoseconds;
    private int _lastNotifiedCount = -1;
    private int _resultsSeen;
    private int _unmatchedResults;
    private int _invalidGazeResults;
    private int _unstableGazeResults;
    private bool _disposed;
    private long _collectionRevision;

    public BaballoniaCalibrator(
        ITrackingDataProvider tracking,
        IBaballoniaTrackingService baballonia,
        ICalibrationOverlay overlay,
        BaballoniaCalibrationStore store,
        ILogService logs)
    {
        _tracking = tracking;
        _baballonia = baballonia;
        _overlay = overlay;
        _store = store;
        _logs = logs;
        _tracking.GazeReceived += OnGazeReceived;
        _tracking.Updated += OnTrackingUpdated;
        _baballonia.Updated += OnBaballoniaUpdated;
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

    public bool IsCollecting
    {
        get
        {
            lock (_gate)
            {
                return _collecting;
            }
        }
    }

    public BaballoniaCalibration? Current => _store.Current;

    public event EventHandler? Changed;

    public void BeginCollection()
    {
        CalibrationOverlayHint? show = null;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_collecting)
            {
                return;
            }

            BeginCollectionLocked(out show);
        }

        ApplyOverlay(show, hide: false);
        Notify();
    }

    public BaballoniaEyeResult Map(BaballoniaEyeResult raw) => _store.Map(raw);

    private void BeginCollectionLocked(out CalibrationOverlayHint? show)
    {
        _collectionRevision++;
        _collecting = true;
        _sawCalibrating = false;
        _nativeCalibrationCompletedAtNanoseconds = null;
        _babbleCollectionStartedAtNanoseconds = null;
        _startedAtNanoseconds = NowLocked();
        _lastNotifiedCount = -1;
        _resultsSeen = 0;
        _unmatchedResults = 0;
        _invalidGazeResults = 0;
        _unstableGazeResults = 0;
        _left.Clear();
        _right.Clear();
        _heads.Clear();
        _pendingResults.Clear();
        _store.Current = null;
        _statusText = "Waiting for Varjo calibration";
        show = OverlayHint(CalibrationOverlayPhase.Waiting, 0);
        _logs.Add(LogLevel.Information, "Babble calibration session started; waiting for native Varjo calibration.");
    }

    private void OnTrackingUpdated(object? sender, EyeTrackingState state)
    {
        CalibrationOverlayHint? show = null;
        var hide = false;
        var changed = false;
        FitWork? fit;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var connected = state.Headset.Connection == ConnectionStatus.Connected;
            var calibrating = state.Headset.IsGazeCalibrating;
            var calibrated = state.Headset.EyeTrackingStatus == "Ready";
            changed = AdvanceLocked(connected, calibrating, calibrated, out show, out hide, out fit);
        }

        ApplyOverlay(show, hide);
        if (fit is not null) FitCalibration(fit);
        if (changed)
        {
            Notify();
        }
    }

    private bool AdvanceLocked(
        bool connected,
        bool calibrating,
        bool calibrated,
        out CalibrationOverlayHint? show,
        out bool hide,
        out FitWork? fit)
    {
        show = null;
        hide = false;
        fit = null;
        if (!connected)
        {
            if (!_collecting)
            {
                return false;
            }

            StopCollectionLocked(fit: false, out hide);
            return true;
        }

        if (calibrating)
        {
            var started = false;
            if (!_collecting)
            {
                BeginCollectionLocked(out show);
                started = true;
            }

            var firstCalibrating = !_sawCalibrating;
            _sawCalibrating = true;
            _nativeCalibrationCompletedAtNanoseconds = null;
            _babbleCollectionStartedAtNanoseconds = null;
            _statusText = "Waiting for Varjo calibration";
            return started || firstCalibrating;
        }

        if (!_collecting)
        {
            return false;
        }

        var now = NowLocked();
        if (_sawCalibrating && calibrated && _nativeCalibrationCompletedAtNanoseconds is null)
        {
            _nativeCalibrationCompletedAtNanoseconds = now;
            _left.Clear();
            _right.Clear();
            _heads.Clear();
            _resultsSeen = 0;
            _unmatchedResults = 0;
            _invalidGazeResults = 0;
            _unstableGazeResults = 0;
            _statusText = "Waiting 7 s for Varjo gaze to settle";
            show = OverlayHint(CalibrationOverlayPhase.Waiting, VarjoSettlingDelay.TotalSeconds, now);
            _logs.Add(LogLevel.Information, "Native calibration completed; waiting 7 seconds before Babble sample collection.");
            return true;
        }

        if (_nativeCalibrationCompletedAtNanoseconds is { } completedAt &&
            _babbleCollectionStartedAtNanoseconds is null &&
            now - completedAt >= VarjoSettlingDelayNanoseconds)
        {
            _babbleCollectionStartedAtNanoseconds = now;
            _sync.Clear();
            _pendingResults.Clear();
            _statusText = "Follow the purple target in the headset (15 s)";
            show = OverlayHint(CalibrationOverlayPhase.Sweep, PostCalibrationWindow.TotalSeconds, now);
            _logs.Add(LogLevel.Information, "Varjo settling period complete; Babble sample collection started.");
            return true;
        }

        if (_babbleCollectionStartedAtNanoseconds is { } collectionStartedAt &&
            now - collectionStartedAt >= PostCalibrationWindowNanoseconds)
        {
            fit = StopCollectionLocked(fit: true, out hide);
            return true;
        }

        if (_startedAtNanoseconds is { } startedAt && now - startedAt > CollectionTimeoutNanoseconds)
        {
            StopCollectionLocked(fit: false, out hide);
            return true;
        }

        return false;
    }

    private FitWork? StopCollectionLocked(bool fit, out bool hide)
    {
        _collectionRevision++;
        hide = true;
        _collecting = false;
        _sawCalibrating = false;
        _nativeCalibrationCompletedAtNanoseconds = null;
        _babbleCollectionStartedAtNanoseconds = null;
        _unmatchedResults += _pendingResults.Count;
        _pendingResults.Clear();
        if (!fit)
        {
            _left.Clear();
            _right.Clear();
            _heads.Clear();
            _statusText = _store.Current is null
                ? "Waiting for Varjo calibration"
                : $"Using {_store.Current.SampleCount} saved samples";
            return null;
        }

        var work = new FitWork(_collectionRevision, [.. _left], [.. _right], [.. _heads],
            _resultsSeen, _unmatchedResults, _invalidGazeResults, _unstableGazeResults);
        _left.Clear();
        _right.Clear();
        _heads.Clear();
        _statusText = "Fitting Babble calibration";
        return work;
    }

    // Only immutable session metadata and owned sample lists escape the collection lock.
    private void FitCalibration(FitWork work)
    {
        var head = BaballoniaCalibrationFitter.FitOutputHead(work.Heads);
        var leftSamples = head is null ? work.Left : BaballoniaCalibrationFitter.ApplyHead(work.Left, head, true);
        var rightSamples = head is null ? work.Right : BaballoniaCalibrationFitter.ApplyHead(work.Right, head, false);
        var left = BaballoniaCalibrationFitter.Fit(leftSamples);
        var right = BaballoniaCalibrationFitter.Fit(rightSamples);
        var count = Math.Min(work.Left.Count, work.Right.Count);
        _logs.Add(LogLevel.Information,
            $"Babble calibration samples: model={work.ResultsSeen}, unmatched={work.UnmatchedResults}, " +
            $"invalid gaze={work.InvalidGazeResults}, unstable gaze={work.UnstableGazeResults}, left={work.Left.Count}, right={work.Right.Count}.");
        if (left is not null && MeanGazeError(leftSamples, left).Calibrated >
            MeanGazeError(leftSamples, BaballoniaEyeCalibration.Identity).Raw + 0.005)
        {
            left = BaballoniaEyeCalibration.Identity;
        }

        if (right is not null && MeanGazeError(rightSamples, right).Calibrated >
            MeanGazeError(rightSamples, BaballoniaEyeCalibration.Identity).Raw + 0.005)
        {
            right = BaballoniaEyeCalibration.Identity;
        }

        var candidate = left is not null && right is not null
            ? new BaballoniaCalibration(left, right, count,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(), BaballoniaCalibration.CurrentPipelineVersion, head)
            : null;
        var fitImproves = true;
        var residualError = 0d;
        if (candidate is not null)
        {
            var leftError = MeanGazeError(leftSamples, candidate.Left);
            var rightError = MeanGazeError(rightSamples, candidate.Right);
            var originalLeft = MeanGazeError(work.Left, BaballoniaEyeCalibration.Identity).Raw;
            var originalRight = MeanGazeError(work.Right, BaballoniaEyeCalibration.Identity).Raw;
            residualError = (leftError.Calibrated + rightError.Calibrated) / 2;
            fitImproves = leftError.Calibrated <= originalLeft + 0.005 &&
                          rightError.Calibrated <= originalRight + 0.005;
            _logs.Add(LogLevel.Information,
                $"Babble fit error (raw → head/affine): left {originalLeft:0.000} → {leftError.Calibrated:0.000}, " +
                $"right {originalRight:0.000} → {rightError.Calibrated:0.000}.");
        }

        var rejected = candidate?.IsPlausible != true || !fitImproves ||
            (candidate.OutputHead is null && candidate.Left.IsIdentity && candidate.Right.IsIdentity);
        lock (_gate)
        {
            // A newer collection or Dispose invalidates this detached fit.
            if (_disposed || _collectionRevision != work.Revision) return;
            if (rejected)
                _statusText = $"Babble calibration rejected ({count} pairs); using raw tracking";
            else
            {
                _store.Current = candidate;
                _statusText = residualError > 0.2
                    ? $"Fitted from {count} pairs; low agreement ({residualError:0.00})"
                    : $"Fitted from {count} paired samples";
            }
        }

        _logs.Add(rejected ? LogLevel.Warning : LogLevel.Information, rejected
            ? $"Babble fit rejected: left={left}, right={right}, pairs={count}."
            : $"Babble gaze map fitted from {count} paired Varjo samples.");
    }

    private sealed record FitWork(long Revision, List<BaballoniaCalibrationSample> Left,
        List<BaballoniaCalibrationSample> Right, List<BaballoniaHeadSample> Heads,
        int ResultsSeen, int UnmatchedResults, int InvalidGazeResults, int UnstableGazeResults);

    private void OnGazeReceived(object? sender, GazeSample sample)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _sync.Push(sample);

            ProcessPendingResultsLocked();
        }
    }

    private void OnBaballoniaUpdated(object? sender, BaballoniaTrackingSample sample)
    {
        if (sample.Result is not { } result)
        {
            return;
        }

        var notify = false;
        var hide = false;
        lock (_gate)
        {
            if (_disposed || !_collecting)
            {
                return;
            }

            var now = NowLocked();
            if (_startedAtNanoseconds is { } startedAt && now - startedAt > CollectionTimeoutNanoseconds)
            {
                StopCollectionLocked(fit: false, out hide);
                notify = true;
            }
            else
            {
                if (_babbleCollectionStartedAtNanoseconds is not { } collectionStartedAt ||
                    result.CaptureTimeNanoseconds < collectionStartedAt)
                {
                    return;
                }

                _resultsSeen++;
                if (result.CaptureTimeNanoseconds <= 0)
                {
                    _unmatchedResults++;
                    return;
                }

                _pendingResults.Enqueue(result);
                notify = ProcessPendingResultsLocked();
            }
        }

        ApplyOverlay(null, hide);
        if (notify)
        {
            Notify();
        }
    }

    private bool ProcessPendingResultsLocked()
    {
        if (!_collecting || _babbleCollectionStartedAtNanoseconds is null)
        {
            return false;
        }

        var notify = false;
        while (_pendingResults.TryPeek(out var result))
        {
            if ((_sync.Count == 0 ||
                 _sync.Latest!.CaptureTimeNanoseconds < result.CaptureTimeNanoseconds + _sync.MaxPairDeltaNanoseconds) &&
                _pendingResults.Count <= SampleCollectionOptions.Calibrator.GazeCapacity)
            {
                break;
            }

            var gaze = _sync.FindNearest(result.CaptureTimeNanoseconds);
            if (gaze is null)
            {
                _pendingResults.Dequeue();
                _unmatchedResults++;
                continue;
            }

            _pendingResults.Dequeue();
            var gazeReason = TrackingSampleValidator.GazeFrame(gaze.Status, gaze.Stability);
            if (gazeReason == SampleRejectionReason.InvalidTrackingStatus)
            {
                _invalidGazeResults++;
                continue;
            }

            if (gazeReason == SampleRejectionReason.LowStability)
            {
                _unstableGazeResults++;
                continue;
            }

            var onnx = result.SmoothedOutput ?? result.RawOutput;
            var addedLeft = TryAdd(_left, result.Left, gaze.Left, gaze.LeftStatus, onnx);
            var addedRight = TryAdd(_right, result.Right, gaze.Right, gaze.RightStatus, onnx);
            if (addedLeft && addedRight && onnx is not null && _heads.Count < MaxSamplesPerEye)
            {
                _heads.Add(new BaballoniaHeadSample(
                    onnx,
                    _left[^1].TargetX, _left[^1].TargetY, _left[^1].TargetOpenness,
                    _right[^1].TargetX, _right[^1].TargetY, _right[^1].TargetOpenness));
            }

            var count = Math.Min(_left.Count, _right.Count);
            if (count != _lastNotifiedCount && (count < 8 || count % 25 == 0))
            {
                _lastNotifiedCount = count;
                _statusText = $"Collecting Babble samples ({count} pairs)";
                notify = true;
            }
        }

        return notify;
    }

    private static bool TryAdd(
        List<BaballoniaCalibrationSample> samples,
        BaballoniaEye babble,
        EyeTrackingEye varjo,
        int eyeStatus,
        BaballoniaRawOutput? onnx)
    {
        if (samples.Count >= MaxSamplesPerEye)
        {
            return false;
        }

        if (!TrackingSampleValidator.IsTrackedEye(eyeStatus))
        {
            return false;
        }

        var targetX = varjo.GazeDirection.X;
        var targetY = -varjo.GazeDirection.Y;
        if (!double.IsFinite(targetX) || !double.IsFinite(targetY) || !double.IsFinite(varjo.Openness))
        {
            return false;
        }

        samples.Add(new BaballoniaCalibrationSample(
            babble.GazeX, babble.GazeY, babble.Openness,
            Math.Clamp(targetX, -1, 1), Math.Clamp(targetY, -1, 1),
            Math.Clamp(varjo.Openness, 0, 1), onnx));
        return true;
    }

    private static (double Raw, double Calibrated) MeanGazeError(
        List<BaballoniaCalibrationSample> samples, BaballoniaEyeCalibration calibration)
    {
        if (samples.Count == 0)
        {
            return (0, 0);
        }

        var rawError = 0d;
        var calibratedError = 0d;
        foreach (var sample in samples)
        {
            rawError += Math.Sqrt(Math.Pow(sample.X - sample.TargetX, 2) +
                                  Math.Pow(sample.Y - sample.TargetY, 2));
            var mapped = calibration.Map(new BaballoniaEye(sample.X, sample.Y, sample.Openness));
            calibratedError += Math.Sqrt(Math.Pow(mapped.GazeX - sample.TargetX, 2) +
                                         Math.Pow(mapped.GazeY - sample.TargetY, 2));
        }

        return (rawError / samples.Count, calibratedError / samples.Count);
    }

    private long NowLocked() => _tracking.GetCurrentTimeNanoseconds();

    private static CalibrationOverlayHint OverlayHint(
        CalibrationOverlayPhase phase, double durationSeconds, long startedAtNanoseconds = 0) =>
        new(phase, 0, 0, 0, durationSeconds, startedAtNanoseconds);

    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);

    private void ApplyOverlay(CalibrationOverlayHint? show, bool hide)
    {
        if (hide)
        {
            _overlay.Hide();
        }
        else if (show is { } hint)
        {
            _overlay.Show(hint);
        }
    }

    public void Dispose()
    {
        var hide = false;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _collectionRevision++;
            if (_collecting)
            {
                StopCollectionLocked(fit: false, out hide);
            }
        }

        ApplyOverlay(null, hide);
        _tracking.GazeReceived -= OnGazeReceived;
        _tracking.Updated -= OnTrackingUpdated;
        _baballonia.Updated -= OnBaballoniaUpdated;
    }
}
