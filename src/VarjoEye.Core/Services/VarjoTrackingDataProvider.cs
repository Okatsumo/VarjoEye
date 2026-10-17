using VarjoEye.Core.Contracts;
using VarjoEye.Core.Models;
using VarjoEye.Core.Services.Varjo;

namespace VarjoEye.Core.Services;

public sealed class VarjoTrackingDataProvider : ITrackingDataProvider
{
    private static readonly TimeSpan DataTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan UiSampleInterval = TimeSpan.FromMilliseconds(33);
    private static readonly TimeSpan DefaultConnectRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(2);

    private readonly IVarjoRuntime _varjo;
    private readonly IAppSettingsService _settings;
    private readonly ILogService _logs;
    private readonly TimeSpan _connectRetryDelay;
    private readonly TimeSpan _stopTimeout;
    private readonly object _gate = new();
    private CancellationTokenSource? _run;
    private Task? _loop;
    private EyeTrackingState _current = EyeTrackingState.Empty;
    private bool _dirty;
    private bool _disposed;
    private string? _lastError;
    private string? _lastCameraError;

    public VarjoTrackingDataProvider(IVarjoRuntime varjo, IAppSettingsService settings, ILogService logs)
        : this(varjo, settings, logs, null, null)
    {
    }

    public VarjoTrackingDataProvider(
        IVarjoRuntime varjo,
        IAppSettingsService settings,
        ILogService logs,
        TimeSpan? connectRetryDelay,
        TimeSpan? stopTimeout)
    {
        _varjo = varjo;
        _settings = settings;
        _logs = logs;
        _connectRetryDelay = connectRetryDelay ?? DefaultConnectRetryDelay;
        _stopTimeout = stopTimeout ?? DefaultStopTimeout;
        _settings.Changed += OnSettingsChanged;
        _varjo.EyeCameraFrameReceived += OnEyeCameraFrame;
    }

    public EyeTrackingState Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public event EventHandler<EyeTrackingState>? Updated;

    public event EventHandler<EyeCameraFrame>? CameraFrameReceived;

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
        }

        _loop = Task.Run(() => PollAsync(run));
    }

    public void Stop() => StopAsync().GetAwaiter().GetResult();

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
        }

        if (run is null)
        {
            return;
        }

        run.Cancel();
        if (loop is not null)
        {
            try
            {
                await loop.WaitAsync(_stopTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _logs.Add(LogLevel.Warning, "Eye tracking poll did not stop within the allotted time.");
            }
            catch (Exception)
            {
            }
        }

        run.Dispose();
        _varjo.Disconnect();
        Publish(EyeTrackingState.Empty);
        NotifyUi();
    }

    public OperationResult RequestCalibration() => _varjo.RequestGazeCalibration();

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

        _settings.Changed -= OnSettingsChanged;
        _varjo.EyeCameraFrameReceived -= OnEyeCameraFrame;
        Stop();
        _varjo.Dispose();
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (_run is null)
            {
                return;
            }
        }

        _ = Task.Run(() =>
        {
            var (frequency, filter) = ReadTrackingMode();
            var result = _varjo.InitializeEyeTracking(frequency, filter);
            if (!result.IsSuccess)
            {
                _logs.Add(LogLevel.Warning, result.ErrorMessage ?? "Failed to apply tracking settings.");
            }

            if (_varjo.IsEyeCameraStreaming)
            {
                var stream = _varjo.StartEyeCameraStream(frequency);
                if (!stream.IsSuccess)
                {
                    _logs.Add(LogLevel.Warning, stream.ErrorMessage ?? "Failed to restart eye camera stream.");
                }
            }
        });
    }

    private async Task PollAsync(CancellationTokenSource run)
    {
        var token = run.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                Publish(Disconnected("Connecting to Varjo runtime…", ConnectionStatus.Connecting));
                NotifyUi();
                var connected = _varjo.Connect();
                if (!connected.IsSuccess)
                {
                    var message = connected.ErrorMessage ?? "Varjo runtime is unavailable.";
                    _logs.Add(LogLevel.Warning, message);
                    Publish(Error(message));
                    NotifyUi();
                    await Task.Delay(_connectRetryDelay, token).ConfigureAwait(false);
                    continue;
                }

                await RunSessionAsync(token).ConfigureAwait(false);
                if (token.IsCancellationRequested)
                {
                    break;
                }

                _varjo.Disconnect();
                await Task.Delay(_connectRetryDelay, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception exception)
        {
            _logs.Add(LogLevel.Error, $"Eye tracking stopped unexpectedly: {exception.Message}");
            Publish(Error(exception.Message));
            NotifyUi();
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_run, run))
                {
                    _run = null;
                    _loop = null;
                }
            }
        }
    }

    private async Task RunSessionAsync(CancellationToken token)
    {
        using var timerResolution = WindowsTimerResolution.Request();
        if (OperatingSystem.IsWindows() && !timerResolution.IsActive)
        {
            _logs.Add(LogLevel.Warning, "Windows did not grant 1 ms timer resolution; gaze polling may run below the requested rate.");
        }

        GazeSample? lastGaze = null;
        long? lastFrame = null;
        var lastGazeAt = DateTimeOffset.UtcNow;
        var lastUiPublish = DateTimeOffset.MinValue;
        var trackingReady = false;
        var cameraReady = false;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(5));
        while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
        {
            var status = _varjo.GetHeadsetStatus();
            if (!status.IsSuccess || status.Status is null)
            {
                var message = status.ErrorMessage ?? "Headset status unavailable.";
                Publish(Error(message), force: message != _lastError);
                _lastError = message;
                trackingReady = false;
                lastGaze = null;
                lastFrame = null;
                StopCameraStream(ref cameraReady);
                continue;
            }

            _lastError = null;
            var headset = status.Status;
            if (!headset.IsConnected)
            {
                trackingReady = false;
                lastGaze = null;
                lastFrame = null;
                StopCameraStream(ref cameraReady);
                Publish(Disconnected("Waiting for headset", ConnectionStatus.Disconnected, headset.ProductName));
                MaybeNotifyUi(ref lastUiPublish);
                continue;
            }

            var (frequency, filter) = ReadTrackingMode();
            EnsureCameraStream(frequency, ref cameraReady);
            if (!trackingReady)
            {
                var init = _varjo.InitializeEyeTracking(frequency, filter);
                if (!init.IsSuccess)
                {
                    var message = init.ErrorMessage ?? "Eye tracking initialization failed.";
                    Publish(BuildHeadsetOnly(headset, ConnectionStatus.Error, "Error", message, hasData: false));
                    MaybeNotifyUi(ref lastUiPublish);
                    continue;
                }

                trackingReady = true;
            }

            var gaze = _varjo.GetGazeSample();
            if (!gaze.IsSuccess)
            {
                var message = gaze.ErrorMessage ?? "Eye tracking data unavailable.";
                Publish(BuildHeadsetOnly(headset, ConnectionStatus.Error, "Error", message, hasData: false));
                MaybeNotifyUi(ref lastUiPublish);
                continue;
            }

            if (gaze.Sample is { } sample && sample.FrameNumber != lastFrame)
            {
                lastFrame = sample.FrameNumber;
                lastGaze = sample;
                lastGazeAt = DateTimeOffset.UtcNow;
                Publish(BuildLive(headset, sample), force: false);
            }
            else if (DateTimeOffset.UtcNow - lastGazeAt > DataTimeout)
            {
                lastGaze = null;
                Publish(BuildHeadsetOnly(headset, ConnectionStatus.Connected, "Waiting", null, hasData: false));
            }
            else if (lastGaze is not null)
            {
                Publish(BuildLive(headset, lastGaze), force: false);
            }
            else
            {
                Publish(BuildHeadsetOnly(headset, ConnectionStatus.Connected, "Waiting", null, hasData: false));
            }

            MaybeNotifyUi(ref lastUiPublish);
        }
    }

    private (EyeTrackingFrequency Frequency, EyeTrackingFilter Filter) ReadTrackingMode()
    {
        var settings = _settings.Current;
        var frequency = settings.OutputFrequencyIndex switch
        {
            1 => EyeTrackingFrequency.Hz100,
            2 => EyeTrackingFrequency.Hz200,
            _ => EyeTrackingFrequency.MaximumSupported
        };
        var filter = settings.RawTrackingMode ? EyeTrackingFilter.Raw : EyeTrackingFilter.Standard;
        return (frequency, filter);
    }

    private void EnsureCameraStream(EyeTrackingFrequency frequency, ref bool cameraReady)
    {
        if (cameraReady)
        {
            return;
        }

        var stream = _varjo.StartEyeCameraStream(frequency);
        cameraReady = true;
        if (!stream.IsSuccess)
        {
            var message = stream.ErrorMessage ?? "Eye camera stream unavailable.";
            if (message != _lastCameraError)
            {
                _lastCameraError = message;
                _logs.Add(LogLevel.Warning, message);
            }

            return;
        }

        _lastCameraError = null;
    }

    private void StopCameraStream(ref bool cameraReady)
    {
        if (!cameraReady && !_varjo.IsEyeCameraStreaming)
        {
            return;
        }

        _varjo.StopEyeCameraStream();
        cameraReady = false;
    }

    private void OnEyeCameraFrame(object? sender, EyeCameraFrame frame) => CameraFrameReceived?.Invoke(this, frame);

    private void Publish(EyeTrackingState state, bool force = true)
    {
        lock (_gate)
        {
            if (!force && ReferenceEquals(_current, state))
            {
                return;
            }

            _current = state;
            _dirty = true;
        }
    }

    private void MaybeNotifyUi(ref DateTimeOffset lastUiPublish)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - lastUiPublish < UiSampleInterval)
        {
            return;
        }

        lastUiPublish = now;
        NotifyUi();
    }

    private void NotifyUi()
    {
        EyeTrackingState state;
        lock (_gate)
        {
            if (!_dirty)
            {
                return;
            }

            _dirty = false;
            state = _current;
        }

        Updated?.Invoke(this, state);
    }

    private static EyeTrackingState Error(string message) =>
        new(
            HeadsetState.For(HeadsetCatalog.Unknown, ConnectionStatus.Error) with
            {
                Name = "No headset",
                ErrorMessage = message,
                RuntimeStatus = "Error"
            },
            CameraState.Waiting("Left Eye Camera"),
            CameraState.Waiting("Right Eye Camera"),
            EyeSourceState.Empty,
            EyeSourceState.Empty,
            false);

    private static EyeTrackingState Disconnected(string runtime, ConnectionStatus connection, string? productName = null)
    {
        var model = connection == ConnectionStatus.Connected
            ? HeadsetCatalog.Resolve(productName)
            : HeadsetCatalog.Unknown;
        var name = connection == ConnectionStatus.Connected ? model.DisplayName : "No headset";
        return new EyeTrackingState(
            HeadsetState.For(model, connection) with { Name = name, RuntimeStatus = runtime },
            CameraState.Waiting("Left Eye Camera"),
            CameraState.Waiting("Right Eye Camera"),
            EyeSourceState.Empty,
            EyeSourceState.Empty,
            false);
    }

    private static EyeTrackingState BuildHeadsetOnly(
        VarjoHeadsetInfo info,
        ConnectionStatus connection,
        string eyeTrackingStatus,
        string? error,
        bool hasData)
    {
        var model = HeadsetCatalog.Resolve(info.ProductName);
        return new EyeTrackingState(
            HeadsetState.FromVarjo(info, model, connection, eyeTrackingStatus, error),
            CameraState.Waiting("Left Eye Camera"),
            CameraState.Waiting("Right Eye Camera"),
            EyeSourceState.Empty,
            EyeSourceState.Empty,
            hasData);
    }

    private static EyeTrackingState BuildLive(VarjoHeadsetInfo info, GazeSample sample)
    {
        var model = HeadsetCatalog.Resolve(info.ProductName);
        var left = MapEye(sample.Left, info.LeftCalibrationQuality);
        var right = MapEye(sample.Right, info.RightCalibrationQuality);
        var eyeStatus = info.IsGazeCalibrating == true
            ? "Calibrating"
            : info.IsGazeCalibrated == true ? "Ready" : "Waiting";
        return new EyeTrackingState(
            HeadsetState.FromVarjo(info, model, ConnectionStatus.Connected, eyeStatus, null),
            CameraState.Waiting("Left Eye Camera"),
            CameraState.Waiting("Right Eye Camera"),
            left,
            right,
            true);
    }

    private static EyeSourceState MapEye(EyeTrackingEye eye, int? calibrationQuality)
    {
        var openness = Math.Clamp(eye.Openness, 0, 1);
        var pupilX = Math.Clamp(eye.GazeDirection.X * 4, -1, 1);
        var pupilY = Math.Clamp(eye.GazeDirection.Y * 4, -1, 1);
        var visual = new EyeSourceVisual(openness, pupilX, pupilY, Math.Clamp(eye.LegacyPupilSize, 0.2, 1.6));
        var varjo = new VarjoEyeMetrics(
            openness,
            eye.PupilDiameterMm,
            eye.IrisDiameterMm,
            eye.PupilIrisRatio,
            eye.LegacyPupilSize,
            FormatCalibration(calibrationQuality),
            eye.GazeOriginMeters,
            eye.GazeDirection);
        return new EyeSourceState(
            visual,
            varjo,
            BabbleEyeMetrics.Empty,
            new CombinedEyeMetrics(openness, pupilX, pupilY));
    }

    private static string FormatCalibration(int? quality) => quality switch
    {
        3 => "High",
        2 => "Medium",
        1 => "Low",
        0 => "Invalid",
        _ => "—"
    };
}
