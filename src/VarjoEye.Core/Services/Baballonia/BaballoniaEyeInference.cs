using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using VarjoEye.Core.Models;

namespace VarjoEye.Core.Services.Baballonia;

internal sealed class BaballoniaEyeInference : IDisposable
{
    internal const int FrameCount = 4;
    private const int Side = BaballoniaFramePreprocessor.Side;
    private readonly InferenceSession _session;
    private readonly string _inputName;
    // Run is serialized by the tracking worker. Reuse its large input buffer, never published outputs.
    private readonly DenseTensor<float> _inputTensor = new([1, 8, Side, Side]);
    private readonly NamedOnnxValue[] _inputs;
    private static readonly string[] TrackingOutputs = [BaballoniaOnnxGraph.GazeOutput];
    private static readonly string[] TrainingOutputs =
        [BaballoniaOnnxGraph.GazeOutput, BaballoniaOnnxGraph.LeftFlatten, BaballoniaOnnxGraph.RightFlatten];
    private readonly Dictionary<long, (float[] Left, float[] Right)> _preparedFrames = new();
    private readonly EyeCropSelector _leftCrop = new();
    private readonly EyeCropSelector _rightCrop = new();
    private long _lastFrameNumber = -1;
    private readonly BaballoniaSmoothingFilter _smoothing = new();
    private bool _disposed;
    private long _lastPreviewAt;

    public BaballoniaInputFramePair? CurrentInputFrame { get; private set; }

    public int CropRevision { get; private set; }

    public string? CropDescription => _leftCrop.Crop is { } left && _rightCrop.Crop is { } right
        ? $"left=({left.X},{left.Y},{left.Size}x{left.Size}), right=({right.X},{right.Y},{right.Size}x{right.Size})"
        : null;

    public BaballoniaEyeInference(string modelPath, InferenceDevice device)
    {
        using var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            InterOpNumThreads = 1,
            IntraOpNumThreads = 1,
            EnableMemoryPattern = device.GpuIndex is null,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
        };
        if (device.GpuIndex is { } gpuIndex)
        {
            options.AppendExecutionProvider_DML(gpuIndex);
        }
        else
        {
            options.AppendExecutionProvider_CPU();
        }

        var bytes = BaballoniaOnnxGraph.WithFlattenOutputs(File.ReadAllBytes(modelPath));
        _session = new InferenceSession(bytes, options);
        var input = _session.InputMetadata.Single();
        _inputName = input.Key;
        _inputs = [NamedOnnxValue.CreateFromTensor(_inputName, _inputTensor)];
        var shape = input.Value.Dimensions;
        if (shape.Length != 4 || shape[1] != 8 || shape[2] != Side || shape[3] != Side)
        {
            throw new InvalidOperationException($"Unsupported Baballonia eye model input: [{string.Join(',', shape)}].");
        }
    }

    public BaballoniaEyeResult? Run(IReadOnlyList<CameraEyeFramePair> window, bool smoothingEnabled = true, double smoothingStrength = 35, GazeSample? cropGaze = null, bool generatePreview = true, bool captureFeatures = false)
    {
        if (window.Count < FrameCount)
        {
            return null;
        }

        var newest = window[window.Count - 1];
        var oldest = window[window.Count - FrameCount];
        if (newest.FrameNumber == _lastFrameNumber) return null;
        if (newest.FrameNumber < _lastFrameNumber)
        {
            _preparedFrames.Clear();
            _smoothing.Reset();
        }

        var captureGap = newest.CaptureTimeNanoseconds > 0 && oldest.CaptureTimeNanoseconds > 0 &&
            newest.CaptureTimeNanoseconds - oldest.CaptureTimeNanoseconds > 80_000_000;
        if (captureGap)
        {
            return null;
        }

        _lastFrameNumber = newest.FrameNumber;
        var previousLeftCrop = _leftCrop.Crop;
        var previousRightCrop = _rightCrop.Crop;
        _leftCrop.Observe(newest.Left, cropGaze);
        _rightCrop.Observe(newest.Right, cropGaze);
        if (previousLeftCrop != _leftCrop.Crop || previousRightCrop != _rightCrop.Crop)
        {
            CropRevision++;
            _lastPreviewAt = 0;
            _preparedFrames.Clear();
            _smoothing.Reset();
        }

        var tensor = _inputTensor;
        for (var frame = 0; frame < FrameCount; frame++)
        {
            var pair = window[window.Count - 1 - frame];
            if (!_preparedFrames.TryGetValue(pair.FrameNumber, out var prepared))
            {
                prepared = (
                    BaballoniaFramePreprocessor.Prepare(pair.Left, _leftCrop.Crop),
                    BaballoniaFramePreprocessor.Prepare(pair.Right, _rightCrop.Crop));
                _preparedFrames.Add(pair.FrameNumber, prepared);
            }

            CopyChannel(prepared.Right, tensor, frame * 2);
            CopyChannel(prepared.Left, tensor, frame * 2 + 1);
        }

        var oldestRetained = oldest.FrameNumber;
        foreach (var frameNumber in _preparedFrames.Keys.Where(number => number < oldestRetained).ToArray())
        {
            _preparedFrames.Remove(frameNumber);
        }

        // Keep one session and the same crop/filter history. Fetch intermediate tensors only for training.
        using var output = _session.Run(_inputs, captureFeatures ? TrainingOutputs : TrackingOutputs);
        float[]? values = null;
        float[]? leftFeatures = null;
        float[]? rightFeatures = null;
        foreach (var named in output)
        {
            var data = named.AsEnumerable<float>().ToArray();
            if (named.Name.Contains("left/Flatten", StringComparison.Ordinal) ||
                named.Name is BaballoniaOnnxGraph.LeftFlatten)
            {
                leftFeatures = data;
            }
            else if (named.Name.Contains("right/Flatten", StringComparison.Ordinal) ||
                     named.Name is BaballoniaOnnxGraph.RightFlatten)
            {
                rightFeatures = data;
            }
            else if (named.Name is BaballoniaOnnxGraph.GazeOutput || data.Length >= 6)
            {
                values = data;
            }
        }

        if (values is null || values.Length < 6 || values.Take(6).Any(value => !float.IsFinite(value)))
        {
            return null;
        }

        if (leftFeatures is { Length: not BaballoniaOnnxGraph.FeatureSize })
        {
            leftFeatures = null;
        }

        if (rightFeatures is { Length: not BaballoniaOnnxGraph.FeatureSize })
        {
            rightFeatures = null;
        }

        var rawOutput = BaballoniaRawOutput.FromArray(values);
        _smoothing.Apply(values, newest.CaptureTimeNanoseconds, smoothingEnabled, smoothingStrength);
        var smoothed = BaballoniaRawOutput.FromArray(values);
        var (left, right) = BaballoniaEyeExpression.FromRaw(values);
        if (!generatePreview)
        {
            CurrentInputFrame = null;
            _lastPreviewAt = 0;
        }
        else
        {
            var now = Stopwatch.GetTimestamp();
            if (_lastPreviewAt == 0 || Stopwatch.GetElapsedTime(_lastPreviewAt, now).TotalMilliseconds >= 33)
            {
                var preview = _preparedFrames[newest.FrameNumber];
                CurrentInputFrame = new BaballoniaInputFramePair(newest.FrameNumber,
                    BaballoniaFramePreprocessor.ToPreviewBytes(preview.Left),
                    BaballoniaFramePreprocessor.ToPreviewBytes(preview.Right), newest.CaptureTimeNanoseconds);
                _lastPreviewAt = now;
            }
        }
        return new BaballoniaEyeResult(
            newest.FrameNumber,
            newest.ReceivedAt,
            left,
            right,
            newest.CaptureTimeNanoseconds,
            rawOutput,
            smoothed,
            leftFeatures,
            rightFeatures);
    }

    private static void CopyChannel(float[] source, DenseTensor<float> target, int channel) =>
        source.AsSpan().CopyTo(target.Buffer.Span.Slice(channel * Side * Side, Side * Side));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.Dispose();
    }
}
