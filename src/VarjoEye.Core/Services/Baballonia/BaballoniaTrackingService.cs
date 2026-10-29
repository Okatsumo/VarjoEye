using System.Threading.Channels;
using VarjoEye.Core.Services.Baballonia.Sampling;
using VarjoEye.Core.Contracts;
using VarjoEye.Core.Models;

namespace VarjoEye.Core.Services.Baballonia;

public sealed class BaballoniaTrackingService : IBaballoniaTrackingService
{
    private static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMilliseconds(250);

    private readonly ITrackingDataProvider _frames;
    private readonly BaballoniaEyeModel _model;
    private readonly IAppSettingsService _settings;
    private readonly ILogService _logs;
    private readonly BaballoniaCalibrationStore _calibration;
    private readonly TimeSpan _stopTimeout;
    private readonly object _gate = new();
    // One in-flight notification and one latest snapshot: slow consumers never build a backlog.
    private readonly Channel<BaballoniaTrackingSample> _updates;
    private readonly Task _updateLoop;
    private long _droppedUpdateCount;
    private readonly Channel<bool> _pending = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        SingleWriter = false,
        // Complete the blocking worker's wake-up directly; never schedule inference on this callback.
        AllowSynchronousContinuations = true,
        FullMode = BoundedChannelFullMode.DropOldest
    });
    private readonly TrackingSampleSynchronizer _cropGaze = new(128, 12_000_000);
    private readonly List<CameraEyeFramePair> _window = [];
    private EyeCameraFrame? _left;
    private EyeCameraFrame? _right;
    private BaballoniaTrackingSample _current = BaballoniaTrackingSample.Empty;
    private BaballoniaEyeInference? _inference;
    private CancellationTokenSource? _run;
    private Task? _loop;
    private bool _cropLogged;
    private int _cropRevision;
    private bool _inferenceFailed;
    private bool _reloadRequested;
    private InferenceDevice _selectedDevice;
    private InferenceDevice? _activeSelection;
    private string _activeDeviceText = "Waiting for camera";
    private bool _disposed;
    private volatile bool _previewEnabled;
    private volatile bool _trainingFeaturesEnabled;

    public BaballoniaTrackingService(
        ITrackingDataProvider frames,
        IAppSettingsService settings,
        ILogService logs,
        BaballoniaCalibrationStore calibration,
        BaballoniaEyeModel model)
        : this(frames, settings, logs, DxgiGpuDiscovery.GetDevices(), DefaultStopTimeout, calibration, model)
    {
    }

    public BaballoniaTrackingService(
        ITrackingDataProvider frames,
        IAppSettingsService settings,
        ILogService logs,
        IReadOnlyList<InferenceDevice> devices,
        TimeSpan? stopTimeout,
        BaballoniaCalibrationStore? calibration,
        BaballoniaEyeModel model)
    {
        _frames = frames;
        _model = model;
        _settings = settings;
        _logs = logs;
        _calibration = calibration ?? new BaballoniaCalibrationStore();
        _stopTimeout = stopTimeout ?? DefaultStopTimeout;
        _updates = Channel.CreateBounded<BaballoniaTrackingSample>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            // Only wake the blocking delivery worker; subscriber code never runs inline.
            AllowSynchronousContinuations = true,
            FullMode = BoundedChannelFullMode.DropOldest
        }, _ => Interlocked.Increment(ref _droppedUpdateCount));
        AvailableDevices = devices.Count > 0 ? devices : [InferenceDevice.Cpu];
        _selectedDevice = ResolveInitialDevice();
        _frames.CameraFrameReceived += OnFrameReceived;
        _frames.GazeReceived += OnCropGaze;
        _settings.Changed += OnSettingsChanged;
        _updateLoop = Task.Factory.StartNew(DispatchUpdates, CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public BaballoniaTrackingSample Current
    {
        get
        {
            lock (_gate)
            {
                return WithFreshness(_current);
            }
        }
    }

    public IReadOnlyList<InferenceDevice> AvailableDevices { get; }

    public InferenceDevice SelectedDevice
    {
        get
        {
            lock (_gate)
            {
                return _selectedDevice;
            }
        }
    }

    public string ActiveDeviceText
    {
        get
        {
            lock (_gate)
            {
                return _activeDeviceText;
            }
        }
    }

    public event EventHandler<BaballoniaTrackingSample>? Updated;

    /// <summary>Snapshots superseded before delivery to subscribers (not camera or inference drops).</summary>
    public long DroppedUpdateCount => Interlocked.Read(ref _droppedUpdateCount);

    public event EventHandler? DevicesChanged;

    public void Start()
    {
        CancellationTokenSource run;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_run is not null)
            {
                return;
            }

            run = new CancellationTokenSource();
            _run = run;
            PublishLocked(Waiting("Initializing"));
        }

        _loop = Task.Factory.StartNew(() => Dispatch(run.Token), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? run;
        Task? loop;
        lock (_gate)
        {
            run = _run;
            loop = _loop;
            _run = null;
            _loop = null;
            if (run is not null)
            {
                PublishLocked(new BaballoniaTrackingSample(
                    BaballoniaRuntimeStatus.Stopping,
                    "Stopping",
                    false,
                    false,
                    null,
                    CameraFrameState.Waiting(EyeSide.Left),
                    CameraFrameState.Waiting(EyeSide.Right)));
            }
        }

        if (run is null)
        {
            return;
        }

        run.Cancel();
        _pending.Writer.TryWrite(true);
        if (loop is not null)
        {
            try
            {
                await loop.WaitAsync(_stopTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _logs.Add(LogLevel.Warning, "Baballonia inference did not stop within the allotted time.");
            }
            catch (Exception)
            {
            }
        }

        run.Dispose();
        lock (_gate)
        {
            DisposeInference();
            _window.Clear();
            _cropGaze.Clear();
            _left = null;
            _right = null;
            PublishLocked(Waiting("Stopped", CameraAvailability.Unavailable));
        }

    }

    public void SetTrainingFeaturesEnabled(bool enabled) => _trainingFeaturesEnabled = enabled;

    public void SetPreviewEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _previewEnabled = enabled;
            if (!enabled)
                _current = _current with
                {
                    LeftPreview = CameraFrameState.Waiting(EyeSide.Left),
                    RightPreview = CameraFrameState.Waiting(EyeSide.Right)
                };
        }
    }

    public void ReloadModel()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _reloadRequested = true;
            _activeDeviceText = "Reloading model…";
        }

        _pending.Writer.TryWrite(true);
        _logs.Add(LogLevel.Information,
            $"Baballonia model reload requested ({(_model.HasUserModel ? "user" : "stock")}: {_model.ResolvedPath}).");
    }

    public void SelectDevice(InferenceDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!AvailableDevices.Contains(device))
            {
                throw new ArgumentException("Unknown inference device.", nameof(device));
            }

            if (_selectedDevice == device)
            {
                return;
            }

            _selectedDevice = device;
            PersistDeviceIndex(device);
            _reloadRequested = true;
            _activeDeviceText = $"Switching to {device.Name}…";
        }

        _pending.Writer.TryWrite(true);
        _logs.Add(LogLevel.Information, $"Baballonia inference device selected: {device.Name}.");
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _frames.CameraFrameReceived -= OnFrameReceived;
        _frames.GazeReceived -= OnCropGaze;
        _settings.Changed -= OnSettingsChanged;
        StopAsync().GetAwaiter().GetResult();
        _pending.Writer.TryComplete();
        _updates.Writer.TryComplete();
        // A subscriber may dispose the service itself; it cannot join its own worker.
        if (Task.CurrentId != _updateLoop.Id && !_updateLoop.Wait(_stopTimeout))
            _logs.Add(LogLevel.Warning, "Baballonia subscriber delivery did not stop within the allotted time.");
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        var index = _settings.Current.InferenceDeviceIndex;
        InferenceDevice device;
        lock (_gate)
        {
            device = DeviceFromIndex(index);
            if (device == _selectedDevice)
            {
                return;
            }
        }

        SelectDevice(device);
    }

    private void OnCropGaze(object? sender, GazeSample sample)
    {
        lock (_gate)
        {
            if (!_disposed && _run is not null) _cropGaze.Push(sample);
        }
    }

    private void OnFrameReceived(object? sender, EyeCameraFrame frame)
    {
        lock (_gate)
        {
            if (_disposed || _run is null)
            {
                return;
            }

            if (frame.IsLeft)
            {
                _left = frame;
            }
            else
            {
                _right = frame;
            }

            if (_left is null || _right is null)
            {
                return;
            }

            if (_left.FrameNumber != _right.FrameNumber)
            {
                if (_left.FrameNumber < _right.FrameNumber)
                {
                    _left = null;
                }
                else
                {
                    _right = null;
                }

                return;
            }

            var pair = new CameraEyeFramePair(frame.FrameNumber, DateTimeOffset.UtcNow, _left, _right);
            _left = null;
            _right = null;
            if (_window.Count > 0)
            {
                var previous = _window[^1];
                var gap = pair.FrameNumber <= previous.FrameNumber ||
                    (pair.CaptureTimeNanoseconds > 0 && previous.CaptureTimeNanoseconds > 0 &&
                     pair.CaptureTimeNanoseconds - previous.CaptureTimeNanoseconds > 40_000_000);
                if (gap)
                {
                    _window.Clear();
                }
            }

            _window.Add(pair);
            while (_window.Count > BaballoniaEyeInference.FrameCount)
            {
                _window.RemoveAt(0);
            }

            _pending.Writer.TryWrite(true);
        }
    }

    private void Dispatch(CancellationToken token)
    {
        try
        {
            while (_pending.Reader.WaitToReadAsync(token).AsTask().GetAwaiter().GetResult())
            {
                if (!_pending.Reader.TryRead(out _)) continue;
                if (token.IsCancellationRequested)
                {
                    break;
                }

                CameraEyeFramePair[] window;
                GazeSample? cropGaze;
                var selected = SelectedDevice;
                var reload = false;
                lock (_gate)
                {
                    reload = _reloadRequested;
                    if (reload)
                    {
                        _reloadRequested = false;
                    }

                    if (_window.Count == 0 && !reload)
                    {
                        continue;
                    }

                    window = [.. _window];
                    cropGaze = window.Length > 0 ? _cropGaze.FindNearest(window[^1].CaptureTimeNanoseconds) : null;
                }

                if (reload || _activeSelection != selected)
                {
                    DisposeInference();
                    _inferenceFailed = false;
                    _cropLogged = false;
                    _activeSelection = selected;
                }

                if (!_inferenceFailed)
                {
                    try
                    {
                        if (_inference is null)
                        {
                            InitializeInference(selected);
                        }

                        var inference = _inference ?? throw new InvalidOperationException("Model session was not created.");
                        if (window.Length > 0 && inference.Run(window, _settings.Current.BaballoniaSmoothingEnabled, _settings.Current.GazeSmoothing, cropGaze, _previewEnabled, _trainingFeaturesEnabled) is { } result)
                        {
                            if (_cropRevision != inference.CropRevision)
                            {
                                _cropRevision = inference.CropRevision;
                                _cropLogged = false;
                                _calibration.Current = null;
                            }
                            if (!_cropLogged && inference.CropDescription is { } cropDescription)
                            {
                                _cropLogged = true;
                                _logs.Add(LogLevel.Information, $"Baballonia fixed camera crop selected: {cropDescription}.");
                            }

                            result = _calibration.Map(result);
                            CameraFrameState leftPreview;
                            CameraFrameState rightPreview;
                            lock (_gate)
                            {
                                if (token.IsCancellationRequested || _selectedDevice != selected)
                                {
                                    continue;
                                }

                                var inputPreview = _previewEnabled ? inference.CurrentInputFrame : null;
                                leftPreview = Preview(EyeSide.Left, inputPreview?.Left, inputPreview?.CaptureTimeNanoseconds ?? 0);
                                rightPreview = Preview(EyeSide.Right, inputPreview?.Right, inputPreview?.CaptureTimeNanoseconds ?? 0);
                                PublishLocked(new BaballoniaTrackingSample(
                                    BaballoniaRuntimeStatus.Tracking,
                                    _activeDeviceText,
                                    true,
                                    false,
                                    result,
                                    leftPreview,
                                    rightPreview));
                            }

                        }
                        else if (window.Length > 0)
                        {
                            lock (_gate)
                            {
                                if (_current.Status is BaballoniaRuntimeStatus.WaitingForCamera or BaballoniaRuntimeStatus.Initializing)
                                {
                                    PublishLocked(Waiting("Waiting for camera"));
                                }
                            }

                        }
                    }
                    catch (Exception exception)
                    {
                        _inferenceFailed = true;
                        SetActiveDeviceText("Model unavailable");
                        _logs.Add(LogLevel.Error, $"Baballonia eye model inference failed: {exception.Message}");
                        lock (_gate)
                        {
                            PublishLocked(new BaballoniaTrackingSample(
                                BaballoniaRuntimeStatus.ModelUnavailable,
                                "Model unavailable",
                                false,
                                false,
                                null,
                                new CameraFrameState(EyeSide.Left, CameraAvailability.Error, Error: exception.Message),
                                new CameraFrameState(EyeSide.Right, CameraAvailability.Error, Error: exception.Message)));
                        }

                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            lock (_gate)
            {
                DisposeInference();
            }
        }
    }

    private void DisposeInference()
    {
        var inference = _inference;
        _inference = null;
        _cropRevision = 0;
        inference?.Dispose();
    }

    private void InitializeInference(InferenceDevice selected)
    {
        var path = _model.ResolvedPath;
        var source = _model.HasUserModel ? "user" : "stock";
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Baballonia model was not found: {path}", path);
        }

        _logs.Add(LogLevel.Information, DescribeModelLoad("loading", path, source, selected));
        _logs.Add(LogLevel.Information, "Baballonia crop setup: look straight ahead with both eyes open. Waiting for stable, synchronized Varjo gaze; full-frame input is used until the fixed crop is verified.");
        try
        {
            _inference = new BaballoniaEyeInference(path, selected);
            SetActiveDeviceText(source == "user"
                ? $"Running on {selected.Name} · trained last layer"
                : $"Running on {selected.Name}");
            _logs.Add(LogLevel.Information, DescribeModelLoad("loaded", path, source, selected));
        }
        catch (Exception exception) when (selected.GpuIndex is not null)
        {
            _logs.Add(LogLevel.Warning,
                $"GPU initialization failed for {selected.Name}; using CPU. {exception.Message}");
            _inference = new BaballoniaEyeInference(path, InferenceDevice.Cpu);
            SetActiveDeviceText(source == "user"
                ? "CPU fallback · trained last layer"
                : $"CPU fallback · {selected.Name} unavailable");
            _logs.Add(LogLevel.Information, DescribeModelLoad("loaded", path, source, InferenceDevice.Cpu));
        }
    }

    private InferenceDevice ResolveInitialDevice()
    {
        var index = _settings.Current.InferenceDeviceIndex;
        if (index >= 0 && index < AvailableDevices.Count)
        {
            return AvailableDevices[index];
        }

        var preferred = AvailableDevices.FirstOrDefault(device => device.GpuIndex is not null)
            ?? InferenceDevice.Cpu;
        PersistDeviceIndex(preferred);
        return preferred;
    }

    private InferenceDevice DeviceFromIndex(int index)
    {
        if (index >= 0 && index < AvailableDevices.Count)
        {
            return AvailableDevices[index];
        }

        return AvailableDevices.FirstOrDefault(device => device.GpuIndex is not null)
            ?? InferenceDevice.Cpu;
    }

    private void PersistDeviceIndex(InferenceDevice device)
    {
        var index = AvailableDevices.ToList().IndexOf(device);
        if (index >= 0)
        {
            _settings.Current.InferenceDeviceIndex = index;
        }
    }

    private void SetActiveDeviceText(string text)
    {
        lock (_gate)
        {
            _activeDeviceText = text;
        }
    }

    private void PublishLocked(BaballoniaTrackingSample sample)
    {
        _current = sample;
        // Called under _gate, so enqueue order matches Current publication order.
        _updates.Writer.TryWrite(sample);
    }

    private static BaballoniaTrackingSample Waiting(string text, CameraAvailability availability = CameraAvailability.Waiting) =>
        new(
            BaballoniaRuntimeStatus.WaitingForCamera,
            text,
            false,
            false,
            null,
            new CameraFrameState(EyeSide.Left, availability),
            new CameraFrameState(EyeSide.Right, availability));

    private static CameraFrameState Preview(EyeSide eye, byte[]? pixels, long timestamp) =>
        pixels is null
            ? CameraFrameState.Waiting(eye)
            : new CameraFrameState(eye, CameraAvailability.Streaming, BaballoniaFramePreprocessor.Side, BaballoniaFramePreprocessor.Side, pixels, timestamp);

    private static BaballoniaTrackingSample WithFreshness(BaballoniaTrackingSample sample)
    {
        if (!sample.HasResult || sample.Result is null)
        {
            return sample;
        }

        var stale = DateTimeOffset.UtcNow - sample.Result.ReceivedAt > StaleAfter;
        return stale == sample.IsStale ? sample : sample with { IsStale = stale };
    }

    private static string DescribeModelLoad(string verb, string path, string source, InferenceDevice device)
    {
        if (!File.Exists(path))
        {
            return $"Baballonia model {verb}: {source}, missing file {path}, device={device.Name}.";
        }

        var info = new FileInfo(path);
        return $"Baballonia model {verb}: {source}, {info.Length} bytes, " +
               $"written {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}, device={device.Name}, path={path}.";
    }

    private void DispatchUpdates()
    {
        while (_updates.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
        {
            if (!_updates.Reader.TryRead(out var sample)) continue;
            sample = WithFreshness(sample);
            var handlers = Updated;
            if (handlers is null) continue;
            foreach (EventHandler<BaballoniaTrackingSample> handler in handlers.GetInvocationList())
            {
                try { handler(this, sample); }
                catch (Exception exception)
                {
                    // A consumer failure must neither disable the model nor skip other subscribers.
                    _logs.Add(LogLevel.Error, $"Baballonia subscriber failed: {exception.Message}");
                }
            }
        }
    }
}
