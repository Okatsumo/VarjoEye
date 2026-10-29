using System.Runtime.InteropServices;
using System.Text;
using VarjoEye.Core.Contracts;
using VarjoEye.Core.Models;

namespace VarjoEye.Core.Services.Varjo;

public sealed class VarjoRuntime : IVarjoRuntime
{
    private readonly ILogService _logs;
    private readonly VarjoNativeMethods.Gaze[] _gazeBuffer = new VarjoNativeMethods.Gaze[256];
    private readonly VarjoNativeMethods.EyeMeasurements[] _measurementBuffer = new VarjoNativeMethods.EyeMeasurements[256];
    private bool _batchGazeUnavailable;
    private readonly object _gate = new();
    private VarjoSessionHandle? _session;
    private bool _eyeTrackingInitialized;
    private EyeTrackingFrequency? _appliedFrequency;
    private EyeTrackingFilter? _appliedFilter;
    private long _eyeCameraStreamId = VarjoNativeMethods.InvalidId;
    private EyeTrackingFrequency? _eyeCameraFrequency;
    private VarjoNativeMethods.FrameListener? _frameListener;
    private int _callbackErrorLogged;
    private bool _disposed;

    public VarjoRuntime(ILogService logs) => _logs = logs;

    public bool IsSessionOpen
    {
        get
        {
            lock (_gate)
            {
                return _session is { IsInvalid: false, IsClosed: false };
            }
        }
    }

    public bool IsEyeTrackingInitialized
    {
        get
        {
            lock (_gate)
            {
                return _eyeTrackingInitialized;
            }
        }
    }

    public bool IsEyeCameraStreaming
    {
        get
        {
            lock (_gate)
            {
                return _eyeCameraStreamId != VarjoNativeMethods.InvalidId;
            }
        }
    }

    public event EventHandler<EyeCameraFrame>? EyeCameraFrameReceived;

    public OperationResult Connect()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_session is not null)
            {
                return OperationResult.Success();
            }

            VarjoSessionHandle? candidate = null;
            try
            {
                if (VarjoNativeMethods.IsAvailable() == 0)
                {
                    return OperationResult.Failure("Varjo runtime is unavailable.");
                }

                var pointer = VarjoNativeMethods.SessionInit();
                if (pointer == IntPtr.Zero)
                {
                    return OperationResult.Failure("Varjo could not create a session.");
                }

                candidate = new VarjoSessionHandle(pointer);
                var error = VarjoNativeMethods.GetError(candidate);
                if (error != 0)
                {
                    return OperationResult.Failure(VarjoNativeMethods.DescribeError(error));
                }

                _session = candidate;
                candidate = null;
                if (VarjoNativeMethods.LoadedLibraryPath is { } libraryPath)
                {
                    _logs.Add(LogLevel.Information, $"Loaded Varjo library: {libraryPath}");
                }

                _logs.Add(LogLevel.Information, "Varjo session opened.");
                return OperationResult.Success();
            }
            catch (DllNotFoundException)
            {
                return OperationResult.Failure("VarjoLib.dll was not found in the Varjo Base installation.");
            }
            catch (EntryPointNotFoundException)
            {
                return OperationResult.Failure("The installed VarjoLib.dll does not expose the required API.");
            }
            catch (BadImageFormatException)
            {
                return OperationResult.Failure("VarjoLib.dll has an incompatible architecture.");
            }
            finally
            {
                candidate?.Dispose();
            }
        }
    }

    public OperationResult InitializeEyeTracking(EyeTrackingFrequency frequency, EyeTrackingFilter filter)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_session is null)
            {
                return OperationResult.Failure("Open a Varjo session first.");
            }

            if (_eyeTrackingInitialized && _appliedFrequency == frequency && _appliedFilter == filter)
            {
                return OperationResult.Success();
            }

            try
            {
                var isAllowed = VarjoNativeMethods.IsGazeAllowed(_session) != 0;
                var error = VarjoNativeMethods.GetError(_session);
                if (error != 0)
                {
                    return OperationResult.Failure(VarjoNativeMethods.DescribeError(error));
                }

                if (!isAllowed)
                {
                    return OperationResult.Failure("Eye tracking is not allowed in Varjo Base.");
                }

                var frequencyValue = frequency switch
                {
                    EyeTrackingFrequency.MaximumSupported => "OutputFrequencyMaximumSupported",
                    EyeTrackingFrequency.Hz100 => "OutputFrequency100Hz",
                    EyeTrackingFrequency.Hz200 => "OutputFrequency200Hz",
                    _ => throw new ArgumentOutOfRangeException(nameof(frequency))
                };
                var filterValue = filter switch
                {
                    EyeTrackingFilter.Standard => "Standard",
                    EyeTrackingFilter.Raw => "None",
                    _ => throw new ArgumentOutOfRangeException(nameof(filter))
                };

                var parameters = new VarjoNativeMethods.GazeParameter[2];
                try
                {
                    parameters[0].Key = Marshal.StringToCoTaskMemUTF8("OutputFrequency");
                    parameters[0].Value = Marshal.StringToCoTaskMemUTF8(frequencyValue);
                    parameters[1].Key = Marshal.StringToCoTaskMemUTF8("OutputFilterType");
                    parameters[1].Value = Marshal.StringToCoTaskMemUTF8(filterValue);
                    VarjoNativeMethods.GazeInitWithParameters(_session, parameters, parameters.Length);
                }
                finally
                {
                    foreach (var parameter in parameters)
                    {
                        Marshal.FreeCoTaskMem(parameter.Key);
                        Marshal.FreeCoTaskMem(parameter.Value);
                    }
                }

                error = VarjoNativeMethods.GetError(_session);
                if (error != 0)
                {
                    return OperationResult.Failure(VarjoNativeMethods.DescribeError(error));
                }

                _eyeTrackingInitialized = true;
                _appliedFrequency = frequency;
                _appliedFilter = filter;
                _logs.Add(LogLevel.Information, $"Eye tracking initialized: frequency={frequency}, filter={filter}.");
                return OperationResult.Success();
            }
            catch (EntryPointNotFoundException)
            {
                return OperationResult.Failure("The installed VarjoLib.dll does not expose the required eye-tracking API.");
            }
        }
    }

    public VarjoHeadsetInfoResult GetHeadsetStatus()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_session is null)
            {
                return VarjoHeadsetInfoResult.Failure("Open a Varjo session first.");
            }

            try
            {
                VarjoNativeMethods.SyncProperties(_session);
                if (VarjoNativeMethods.HasProperty(_session, VarjoNativeMethods.HmdConnectedPropertyKey) == 0)
                {
                    InvalidateEyeTracking();
                    return VarjoHeadsetInfoResult.Failure("Varjo did not provide headset connection status.");
                }

                var isConnected = VarjoNativeMethods.GetPropertyBool(_session, VarjoNativeMethods.HmdConnectedPropertyKey) != 0;
                if (!isConnected)
                {
                    InvalidateEyeTracking();
                }

                string? productName = null;
                if (isConnected &&
                    VarjoNativeMethods.HasProperty(_session, VarjoNativeMethods.HmdProductNamePropertyKey) != 0)
                {
                    var size = VarjoNativeMethods.GetPropertyStringSize(_session, VarjoNativeMethods.HmdProductNamePropertyKey);
                    if (size is > 1 and <= 1024)
                    {
                        var buffer = new byte[(int)size];
                        VarjoNativeMethods.GetPropertyString(
                            _session,
                            VarjoNativeMethods.HmdProductNamePropertyKey,
                            buffer,
                            size);
                        var length = Array.IndexOf(buffer, (byte)0);
                        productName = Encoding.UTF8.GetString(buffer, 0, length < 0 ? buffer.Length : length);
                    }
                }

                bool? isCalibrating = null;
                bool? isCalibrated = null;
                bool? isUserPresent = null;
                bool? isGazeAllowed = null;
                int? leftCalibrationQuality = null;
                int? rightCalibrationQuality = null;
                if (isConnected)
                {
                    if (VarjoNativeMethods.HasProperty(_session, VarjoNativeMethods.GazeCalibratingPropertyKey) != 0)
                    {
                        isCalibrating = VarjoNativeMethods.GetPropertyBool(_session, VarjoNativeMethods.GazeCalibratingPropertyKey) != 0;
                    }

                    if (VarjoNativeMethods.HasProperty(_session, VarjoNativeMethods.GazeCalibratedPropertyKey) != 0)
                    {
                        isCalibrated = VarjoNativeMethods.GetPropertyBool(_session, VarjoNativeMethods.GazeCalibratedPropertyKey) != 0;
                    }

                    if (VarjoNativeMethods.HasProperty(_session, VarjoNativeMethods.UserPresencePropertyKey) != 0)
                    {
                        isUserPresent = VarjoNativeMethods.GetPropertyBool(_session, VarjoNativeMethods.UserPresencePropertyKey) != 0;
                    }

                    if (VarjoNativeMethods.HasProperty(_session, VarjoNativeMethods.GazeAllowedPropertyKey) != 0)
                    {
                        isGazeAllowed = VarjoNativeMethods.GetPropertyBool(_session, VarjoNativeMethods.GazeAllowedPropertyKey) != 0;
                    }

                    if (VarjoNativeMethods.HasProperty(_session, VarjoNativeMethods.GazeCalibrationQualityLeftPropertyKey) != 0)
                    {
                        leftCalibrationQuality = VarjoNativeMethods.GetPropertyInt(
                            _session,
                            VarjoNativeMethods.GazeCalibrationQualityLeftPropertyKey);
                    }

                    if (VarjoNativeMethods.HasProperty(_session, VarjoNativeMethods.GazeCalibrationQualityRightPropertyKey) != 0)
                    {
                        rightCalibrationQuality = VarjoNativeMethods.GetPropertyInt(
                            _session,
                            VarjoNativeMethods.GazeCalibrationQualityRightPropertyKey);
                    }
                }

                var error = VarjoNativeMethods.GetError(_session);
                if (error != 0)
                {
                    InvalidateEyeTracking();
                    return VarjoHeadsetInfoResult.Failure(VarjoNativeMethods.DescribeError(error));
                }

                string? runtimeVersion = null;
                try
                {
                    runtimeVersion = VarjoNativeMethods.GetVersionString();
                }
                catch (EntryPointNotFoundException)
                {
                }

                return VarjoHeadsetInfoResult.Success(new VarjoHeadsetInfo(
                    isConnected,
                    productName,
                    isCalibrated,
                    isCalibrating,
                    isUserPresent,
                    isGazeAllowed,
                    leftCalibrationQuality,
                    rightCalibrationQuality,
                    runtimeVersion));
            }
            catch (EntryPointNotFoundException)
            {
                return VarjoHeadsetInfoResult.Failure("The installed VarjoLib.dll does not expose headset status properties.");
            }
        }
    }

    public VarjoGazeResult GetGazeSample()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_session is null || !_eyeTrackingInitialized)
            {
                return VarjoGazeResult.Failure("Eye tracking is not initialized.");
            }

            try
            {
                var hasData = VarjoNativeMethods.GetGazeData(_session, out var gaze, out var measurements) != 0;
                var error = VarjoNativeMethods.GetError(_session);
                if (error != 0)
                {
                    return VarjoGazeResult.Failure(VarjoNativeMethods.DescribeError(error));
                }

                if (!hasData)
                {
                    return VarjoGazeResult.Success(null);
                }

                return VarjoGazeResult.Success(ConvertGaze(gaze, measurements));
            }
            catch (EntryPointNotFoundException)
            {
                return VarjoGazeResult.Failure("The installed VarjoLib.dll does not expose gaze data API.");
            }
        }
    }

    public VarjoGazeBatchResult GetGazeSamples()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_session is null || !_eyeTrackingInitialized)
                return new([], "Eye tracking is not initialized.");
            if (_batchGazeUnavailable)
            {
                var latest = GetGazeSample();
                return new(latest.Sample is { } sample ? [sample] : [], latest.ErrorMessage);
            }
            try
            {
                var count = VarjoNativeMethods.GetGazeDataArray(_session, _gazeBuffer, _measurementBuffer, _gazeBuffer.Length);
                var error = VarjoNativeMethods.GetError(_session);
                if (error != 0) return new([], VarjoNativeMethods.DescribeError(error));
                if (count < 0 || count > _gazeBuffer.Length) return new([], "Invalid gaze batch size from Varjo.");
                if (count == 0) return new([]);
                var samples = new GazeSample[count];
                for (var i = 0; i < count; i++) samples[i] = ConvertGaze(_gazeBuffer[i], _measurementBuffer[i]);
                return new(samples);
            }
            catch (EntryPointNotFoundException)
            {
                _batchGazeUnavailable = true;
                _logs.Add(LogLevel.Warning, "Varjo batch gaze API unavailable; using latest-only gaze polling. Intermediate frames may be lost.");
                var latest = GetGazeSample();
                return new(latest.Sample is { } sample ? [sample] : [], latest.ErrorMessage);
            }
        }
    }

    private static GazeSample ConvertGaze(VarjoNativeMethods.Gaze gaze, VarjoNativeMethods.EyeMeasurements measurements) =>
        new(
            gaze.FrameNumber,
            ConvertEye(gaze.LeftEye, gaze.DeprecatedLeftPupilSize, measurements.LeftOpenness,
                measurements.LeftPupilDiameterMm, measurements.LeftIrisDiameterMm, measurements.LeftPupilIrisRatio),
            ConvertEye(gaze.RightEye, gaze.DeprecatedRightPupilSize, measurements.RightOpenness,
                measurements.RightPupilDiameterMm, measurements.RightIrisDiameterMm, measurements.RightPupilIrisRatio),
            gaze.CaptureTime,
            (int)gaze.Status,
            gaze.Stability,
            (int)gaze.LeftStatus,
            (int)gaze.RightStatus);

    public long GetCurrentTimeNanoseconds()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_session is null)
            {
                return 0;
            }

            try
            {
                return VarjoNativeMethods.GetCurrentTime(_session);
            }
            catch (EntryPointNotFoundException)
            {
                return 0;
            }
        }
    }

    public OperationResult RequestGazeCalibration()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_session is null)
            {
                return OperationResult.Failure("Open a Varjo session first.");
            }

            try
            {
                VarjoNativeMethods.SyncProperties(_session);
                if (VarjoNativeMethods.HasProperty(_session, VarjoNativeMethods.HmdConnectedPropertyKey) == 0 ||
                    VarjoNativeMethods.GetPropertyBool(_session, VarjoNativeMethods.HmdConnectedPropertyKey) == 0)
                {
                    return OperationResult.Failure("No Varjo headset is connected.");
                }

                if (VarjoNativeMethods.IsGazeAllowed(_session) == 0)
                {
                    return OperationResult.Failure("Eye tracking is not allowed in Varjo Base.");
                }

                var error = VarjoNativeMethods.GetError(_session);
                if (error != 0)
                {
                    return OperationResult.Failure(VarjoNativeMethods.DescribeError(error));
                }

                VarjoNativeMethods.RequestGazeCalibration(_session);
                error = VarjoNativeMethods.GetError(_session);
                if (error != 0)
                {
                    return OperationResult.Failure(VarjoNativeMethods.DescribeError(error));
                }

                _logs.Add(LogLevel.Information, "Native Varjo gaze calibration requested.");
                return OperationResult.Success();
            }
            catch (EntryPointNotFoundException)
            {
                return OperationResult.Failure("The installed VarjoLib.dll does not expose gaze calibration API.");
            }
        }
    }

    public OperationResult StartEyeCameraStream(EyeTrackingFrequency frequency)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_session is null)
            {
                return OperationResult.Failure("Open a Varjo session first.");
            }

            if (_eyeCameraStreamId != VarjoNativeMethods.InvalidId && _eyeCameraFrequency == frequency)
            {
                return OperationResult.Success();
            }

            StopEyeCameraStreamCore();

            try
            {
                var count = VarjoNativeMethods.GetDataStreamConfigCount(_session);
                if (count is <= 0 or > 128)
                {
                    return OperationResult.Failure("No Varjo data streams are available.");
                }

                var configSize = Marshal.SizeOf<VarjoNativeMethods.StreamConfig>();
                var configs = Marshal.AllocHGlobal(checked(count * configSize));
                try
                {
                    VarjoNativeMethods.GetDataStreamConfigs(_session, configs, count);
                    var error = VarjoNativeMethods.GetError(_session);
                    if (error != 0)
                    {
                        return OperationResult.Failure(VarjoNativeMethods.DescribeError(error));
                    }

                    var options = new List<EyeCameraStreamOption>(count);
                    for (var i = 0; i < count; i++)
                    {
                        var config = Marshal.PtrToStructure<VarjoNativeMethods.StreamConfig>(
                            IntPtr.Add(configs, i * configSize));
                        if (config.StreamType != VarjoNativeMethods.EyeCameraStreamType ||
                            config.BufferType != VarjoNativeMethods.CpuBufferType ||
                            (config.ChannelFlags & 3) != 3)
                        {
                            continue;
                        }

                        options.Add(new EyeCameraStreamOption(
                            config.StreamId,
                            config.ChannelFlags,
                            config.FrameRate,
                            config.Width,
                            config.Height));
                    }

                    var selected = EyeCameraStreamSelector.Select(options, frequency);
                    if (selected is null)
                    {
                        return OperationResult.Failure("Eye camera stream is unavailable. Varjo Base Pro may be required.");
                    }

                    _frameListener = OnEyeCameraFrame;
                    VarjoNativeMethods.StartDataStream(_session, selected.Value.StreamId, 3, _frameListener, IntPtr.Zero);
                    error = VarjoNativeMethods.GetError(_session);
                    if (error != 0)
                    {
                        _frameListener = null;
                        return OperationResult.Failure(VarjoNativeMethods.DescribeError(error));
                    }

                    _eyeCameraStreamId = selected.Value.StreamId;
                    _eyeCameraFrequency = frequency;
                    _logs.Add(
                        LogLevel.Information,
                        $"Eye camera stream started ({selected.Value.Width}x{selected.Value.Height}, {selected.Value.FrameRate} Hz).");
                    return OperationResult.Success();
                }
                finally
                {
                    Marshal.FreeHGlobal(configs);
                }
            }
            catch (EntryPointNotFoundException)
            {
                return OperationResult.Failure("The installed VarjoLib.dll does not expose the eye camera stream API.");
            }
        }
    }

    public void StopEyeCameraStream()
    {
        lock (_gate)
        {
            StopEyeCameraStreamCore();
        }
    }

    public void Disconnect()
    {
        lock (_gate)
        {
            StopEyeCameraStreamCore();
            var wasConnected = _session is not null;
            InvalidateEyeTracking();
            _session?.Dispose();
            _session = null;
            if (wasConnected)
            {
                _logs.Add(LogLevel.Information, "Varjo session closed.");
            }
        }
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

        Disconnect();
    }

    private void InvalidateEyeTracking()
    {
        _eyeTrackingInitialized = false;
        _appliedFrequency = null;
        _appliedFilter = null;
    }

    private void StopEyeCameraStreamCore()
    {
        if (_eyeCameraStreamId == VarjoNativeMethods.InvalidId || _session is null)
        {
            return;
        }

        VarjoNativeMethods.StopDataStream(_session, _eyeCameraStreamId);
        _eyeCameraStreamId = VarjoNativeMethods.InvalidId;
        _eyeCameraFrequency = null;
        _frameListener = null;
        _logs.Add(LogLevel.Information, "Eye camera stream stopped.");
    }

    private void OnEyeCameraFrame(IntPtr frame, IntPtr session, IntPtr userData)
    {
        try
        {
            var streamType = Marshal.ReadInt64(frame, 0);
            var streamId = Marshal.ReadInt64(frame, 8);
            var frameNumber = Marshal.ReadInt64(frame, 16);
            var channels = (ulong)Marshal.ReadInt64(frame, 24);
            var dataFlags = (ulong)Marshal.ReadInt64(frame, 32);
            if (streamType != VarjoNativeMethods.EyeCameraStreamType || (dataFlags & 1) == 0)
            {
                return;
            }

            var captureTime = Marshal.ReadInt64(frame, VarjoNativeMethods.StreamFrameEyeCameraTimestampOffset);
            for (var channel = 0; channel < 2; channel++)
            {
                if ((channels & (1UL << channel)) == 0)
                {
                    continue;
                }

                var bufferId = VarjoNativeMethods.GetBufferId(session, streamId, frameNumber, channel);
                if (bufferId == VarjoNativeMethods.InvalidId)
                {
                    continue;
                }

                EyeCameraFrame? managedFrame = null;
                VarjoNativeMethods.LockDataStreamBuffer(session, bufferId);
                try
                {
                    var metadata = VarjoNativeMethods.GetBufferMetadata(session, bufferId);
                    if (metadata.Type != VarjoNativeMethods.CpuBufferType ||
                        metadata.Width is <= 0 or > 4096 ||
                        metadata.Height is <= 0 or > 4096 ||
                        metadata.RowStride < metadata.Width ||
                        (long)metadata.RowStride * metadata.Height > metadata.ByteSize)
                    {
                        continue;
                    }

                    var source = VarjoNativeMethods.GetBufferCPUData(session, bufferId);
                    if (source == IntPtr.Zero)
                    {
                        continue;
                    }

                    var pixels = CopyCameraPixels(source, metadata.Width, metadata.Height, metadata.RowStride);
                    managedFrame = new EyeCameraFrame(channel == 0, metadata.Width, metadata.Height,
                        pixels, frameNumber, captureTime);
                }
                finally
                {
                    VarjoNativeMethods.UnlockDataStreamBuffer(session, bufferId);
                }
                // Only managed memory escapes the lock; subscribers cannot retain an SDK buffer.
                if (managedFrame is not null) EyeCameraFrameReceived?.Invoke(this, managedFrame);
            }
        }
        catch (Exception)
        {
            if (Interlocked.Exchange(ref _callbackErrorLogged, 1) == 0)
            {
                _logs.Add(LogLevel.Error, "Eye camera frame processing failed.");
            }
        }
    }

    internal static byte[] CopyCameraPixels(IntPtr source, int width, int height, int rowStride)
    {
        var pixels = new byte[checked(width * height)];
        if (rowStride == width)
        {
            Marshal.Copy(source, pixels, 0, pixels.Length);
        }
        else
        {
            for (var row = 0; row < height; row++)
                Marshal.Copy(IntPtr.Add(source, row * rowStride), pixels, row * width, width);
        }
        return pixels;
    }

    private static EyeTrackingEye ConvertEye(
        VarjoNativeMethods.GazeRay ray,
        double legacyPupil,
        float openness,
        float pupilMm,
        float irisMm,
        float ratio) =>
        new(
            openness,
            pupilMm,
            irisMm,
            ratio,
            legacyPupil,
            new Vector3Value(ray.OriginX, ray.OriginY, ray.OriginZ),
            new Vector3Value(ray.DirectionX, ray.DirectionY, ray.DirectionZ));
}
