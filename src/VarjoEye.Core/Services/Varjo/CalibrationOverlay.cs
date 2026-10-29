using System;
using System.Runtime.InteropServices;
using System.Threading;
using VarjoEye.Core.Contracts;
using VarjoEye.Core.Models;

namespace VarjoEye.Core.Services.Varjo;

/// <summary>
/// Separate high-priority Varjo session that submits a face-locked HUD layer.
/// OpenVR/OpenXR stay at priority 0, so this sits on top of either runtime.
/// </summary>
public sealed class CalibrationOverlay : ICalibrationOverlay
{
    private const int TextureSize = 1024;
    private const int SwapImages = 3;
    private const int SessionPriority = VarjoNativeMethods.OverlaySessionPriority;
    private static readonly (double X, double Y)[] SweepPath =
    [
        (0, 0), (0.72, 0), (0.5, 0.5), (0, 0.72), (-0.5, 0.5),
        (-0.72, 0), (-0.5, -0.55), (0, -0.75), (0.5, -0.55), (0.72, 0), (0, 0),
    ];

    private readonly ILogService _logs;
    private readonly IVarjoRuntime _varjo;
    private readonly object _gate = new();
    private CalibrationOverlayHint _hint = new(CalibrationOverlayPhase.Hidden, 0, 0, 0);
    private string _statusText = "Overlay idle";
    private Thread? _thread;
    private Timer? _hintTimer;
    private bool _stop;
    private bool _failed;
    private bool _disposed;
    private bool _wantRunning;

    public CalibrationOverlay(ILogService logs, IVarjoRuntime varjo)
    {
        _logs = logs;
        _varjo = varjo;
    }

    public string StatusText
    {
        get { lock (_gate) return _statusText; }
    }

    public CalibrationOverlayHint CurrentHint
    {
        get { lock (_gate) return CurrentHintLocked(); }
    }

    public event EventHandler? Changed;

    public void Show(CalibrationOverlayHint hint)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (hint.PhaseStartedNanoseconds <= 0)
            {
                var started = _hint.Phase == hint.Phase && _hint.PhaseStartedNanoseconds > 0
                    ? _hint.PhaseStartedNanoseconds
                    : _varjo.GetCurrentTimeNanoseconds();
                hint = hint with { PhaseStartedNanoseconds = started };
            }
            _hint = hint;
            if (_failed)
            {
                return;
            }

            _wantRunning = true;
            EnsureRenderThreadLocked();
        }

        _hintTimer ??= new Timer(_ => Changed?.Invoke(this, EventArgs.Empty), null, 50, 50);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureRenderThreadLocked()
    {
        if (_failed || _disposed || !_wantRunning)
        {
            return;
        }

        _stop = false;
        if (_thread is { IsAlive: true })
        {
            return;
        }

        _thread = new Thread(RenderLoop) { IsBackground = true, Name = "VarjoCalibrationOverlay" };
        _thread.Start();
    }

    public void Hide()
    {
        lock (_gate)
        {
            _hint = new(CalibrationOverlayPhase.Hidden, 0, 0, 0);
            _wantRunning = false;
            _stop = true;
            _hintTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RenderLoop()
    {
        VarjoSessionHandle? session = null;
        IntPtr frameInfo = IntPtr.Zero;
        IntPtr swapChain = IntPtr.Zero;
        IntPtr device = IntPtr.Zero;
        IntPtr context = IntPtr.Zero;
        IntPtr[] textures = [];
        try
        {
            if (VarjoNativeMethods.IsAvailable() == 0)
                throw new InvalidOperationException("Varjo runtime is unavailable.");
            var pointer = VarjoNativeMethods.SessionInit();
            if (pointer == IntPtr.Zero)
                throw new InvalidOperationException("Varjo could not create an overlay session.");
            session = new VarjoSessionHandle(pointer);
            ThrowIfError(session, "SessionInit");
            VarjoNativeMethods.SessionSetPriority(session, SessionPriority);
            ThrowIfError(session, "SessionSetPriority");

            var luid = VarjoNativeMethods.D3D11GetLuid(session);
            ThrowIfError(session, "D3D11GetLuid");
            D3D11.CreateDevice(luid.ToInt64(), out device, out context);

            var config = new VarjoNativeMethods.SwapChainConfig2
            {
                TextureFormat = VarjoNativeMethods.TextureFormatRgbaSrgb,
                NumberOfTextures = SwapImages,
                TextureWidth = TextureSize,
                TextureHeight = TextureSize,
                TextureArraySize = 1,
            };
            swapChain = VarjoNativeMethods.D3D11CreateSwapChain(session, device, in config);
            if (swapChain == IntPtr.Zero) ThrowIfError(session, "D3D11CreateSwapChain");
            ThrowIfError(session, "D3D11CreateSwapChain");

            textures = new IntPtr[SwapImages];
            for (var i = 0; i < SwapImages; i++)
            {
                textures[i] = VarjoNativeMethods.ToD3D11Texture(VarjoNativeMethods.GetSwapChainImage(swapChain, i));
                if (textures[i] == IntPtr.Zero)
                    throw new InvalidOperationException($"Swap chain image {i} is missing.");
            }

            frameInfo = VarjoNativeMethods.CreateFrameInfo(session);
            if (frameInfo == IntPtr.Zero) throw new InvalidOperationException("CreateFrameInfo failed.");
            var viewCount = Math.Max(VarjoNativeMethods.GetViewCount(session), 1);
            ThrowIfError(session, "GetViewCount");
            var useView = new bool[viewCount];
            var isLeftEye = new bool[viewCount];
            var contextViews = 0;
            for (var i = 0; i < viewCount; i++)
            {
                var description = VarjoNativeMethods.GetViewDescription(session, i);
                useView[i] = description.Display == VarjoNativeMethods.DisplayTypeContext;
                isLeftEye[i] = description.Eye != VarjoNativeMethods.EyeRight;
                if (useView[i]) contextViews++;
            }
            if (contextViews == 0)
                Array.Fill(useView, true);

            lock (_gate) _statusText = "Overlay running";
            _logs.Add(LogLevel.Information,
                $"Varjo HUD overlay started (priority={SessionPriority}, views={viewCount}, context={contextViews}).");

            var pixels = new byte[TextureSize * TextureSize * 4];
            var views = new VarjoNativeMethods.LayerMultiProjView[viewCount];
            var viewsHandle = GCHandle.Alloc(views, GCHandleType.Pinned);
            var pixelsHandle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                while (true)
                {
                    CalibrationOverlayHint hint;
                    lock (_gate)
                    {
                        if (_stop || _disposed) break;
                        hint = CurrentHintLocked();
                    }

                    VarjoNativeMethods.WaitSync(session, frameInfo);
                    var info = Marshal.PtrToStructure<VarjoNativeMethods.FrameInfo>(frameInfo);
                    var headPose = VarjoNativeMethods.FrameGetPose(session, VarjoNativeMethods.PoseTypeCenter);
                    Array.Clear(pixels);
                    VarjoNativeMethods.BeginFrameWithLayers(session);
                    VarjoNativeMethods.AcquireSwapChainImage(swapChain, out var index);

                    var submitted = 0;
                    var half = TextureSize / 2;
                    for (var i = 0; i < viewCount; i++)
                    {
                        if (!useView[i]) continue;
                        var viewPtr = IntPtr.Add(info.Views, i * VarjoNativeMethods.ViewInfoSize);
                        if (Marshal.ReadInt32(viewPtr, VarjoNativeMethods.ViewInfoEnabledOffset) == 0) continue;
                        var projectionValues = new double[16];
                        var viewValues = new double[16];
                        Marshal.Copy(viewPtr, projectionValues, 0, 16);
                        Marshal.Copy(IntPtr.Add(viewPtr, VarjoNativeMethods.ViewInfoViewOffset), viewValues, 0, 16);
                        var projection = VarjoNativeMethods.VarjoMatrix.FromDoubles(projectionValues);
                        var worldToEye = VarjoNativeMethods.VarjoMatrix.FromDoubles(viewValues);
                        var x = isLeftEye[i] ? 0 : half;
                        PaintEye(pixels, hint, projection, worldToEye, headPose, x, 0, half, TextureSize);
                        views[submitted] = new VarjoNativeMethods.LayerMultiProjView
                        {
                            Projection = projection,
                            View = worldToEye,
                            Viewport = new VarjoNativeMethods.SwapChainViewport
                            {
                                SwapChain = swapChain,
                                X = x,
                                Width = half,
                                Height = TextureSize,
                            },
                        };
                        submitted++;
                    }
                    if ((uint)index < (uint)textures.Length)
                        D3D11.UpdateSubresource(context, textures[index], pixelsHandle.AddrOfPinnedObject(), TextureSize * 4);

                    VarjoNativeMethods.ReleaseSwapChainImage(swapChain);
                    if (submitted == 0)
                    {
                        var empty = new VarjoNativeMethods.SubmitInfoLayers { FrameNumber = info.FrameNumber };
                        VarjoNativeMethods.EndFrameWithLayers(session, in empty);
                        continue;
                    }

                    var layer = new VarjoNativeMethods.LayerMultiProj
                    {
                        Header = new VarjoNativeMethods.LayerHeader
                        {
                            Type = VarjoNativeMethods.LayerMultiProjType,
                            Flags = VarjoNativeMethods.LayerFlagAlphaBlend,
                        },
                        Space = VarjoNativeMethods.SpaceLocal,
                        ViewCount = submitted,
                        Views = viewsHandle.AddrOfPinnedObject(),
                    };
                    var layerPtr = Marshal.AllocHGlobal(Marshal.SizeOf<VarjoNativeMethods.LayerMultiProj>());
                    var layersPtr = Marshal.AllocHGlobal(IntPtr.Size);
                    try
                    {
                        Marshal.StructureToPtr(layer, layerPtr, false);
                        Marshal.WriteIntPtr(layersPtr, layerPtr);
                        var submit = new VarjoNativeMethods.SubmitInfoLayers
                        {
                            FrameNumber = info.FrameNumber,
                            LayerCount = 1,
                            Layers = layersPtr,
                        };
                        VarjoNativeMethods.EndFrameWithLayers(session, in submit);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(layersPtr);
                        Marshal.FreeHGlobal(layerPtr);
                    }
                }
            }
            finally
            {
                viewsHandle.Free();
                pixelsHandle.Free();
            }
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                _failed = true;
                _statusText = "Overlay unavailable";
            }
            _logs.Add(LogLevel.Warning, $"Varjo HUD overlay failed; calibration continues without headset prompts. {exception.Message}");
        }
        finally
        {
            if (frameInfo != IntPtr.Zero) VarjoNativeMethods.FreeFrameInfo(frameInfo);
            if (swapChain != IntPtr.Zero) VarjoNativeMethods.FreeSwapChain(swapChain);
            foreach (var texture in textures)
                if (texture != IntPtr.Zero) Marshal.Release(texture);
            if (context != IntPtr.Zero) Marshal.Release(context);
            if (device != IntPtr.Zero) Marshal.Release(device);
            session?.Dispose();
            lock (_gate)
            {
                if (!_failed) _statusText = "Overlay idle";
                _thread = null;
                if (_wantRunning && !_disposed && !_failed)
                {
                    EnsureRenderThreadLocked();
                }
            }
        }
    }

    private CalibrationOverlayHint CurrentHintLocked()
    {
        var hint = _hint;
        if (hint.Phase is not (CalibrationOverlayPhase.Sweep or CalibrationOverlayPhase.Blink))
            return hint;
        var now = _varjo.GetCurrentTimeNanoseconds();
        var started = hint.PhaseStartedNanoseconds;
        var elapsed = now > 0 && started > 0 ? Math.Max(0, (now - started) / 1_000_000_000d) : 0;
        var span = hint.DurationSeconds > 0 ? hint.DurationSeconds : 15d;
        var progress = Math.Clamp(elapsed / span, 0, 1);
        if (hint.Phase == CalibrationOverlayPhase.Blink)
            return hint with { TargetX = 0, TargetY = 0, Progress = progress };
        var position = progress * (SweepPath.Length - 1);
        var index = Math.Min((int)position, SweepPath.Length - 2);
        var blend = position - index;
        var from = SweepPath[index];
        var to = SweepPath[index + 1];
        return hint with
        {
            TargetX = from.X + (to.X - from.X) * blend,
            TargetY = from.Y + (to.Y - from.Y) * blend,
            Progress = progress,
        };
    }

    private const double TargetDistance = 1.2;

    private static void PaintEye(byte[] pixels, CalibrationOverlayHint hint,
        in VarjoNativeMethods.VarjoMatrix projection, in VarjoNativeMethods.VarjoMatrix worldToEye,
        in VarjoNativeMethods.VarjoMatrix headPose, int originX, int originY, int width, int height)
    {
        if (hint.Phase == CalibrationOverlayPhase.Hidden) return;
        var hasCenter = TryProject(0, 0, projection, worldToEye, headPose, originX, originY, width, height, out var cx, out var cy);
        if (hasCenter)
            DrawCircle(pixels, cx, cy, 10, originX, originY, width, height, 40, 40, 48, 90);
        foreach (var point in SweepPath)
        {
            if (TryProject(point.X, point.Y, projection, worldToEye, headPose, originX, originY, width, height, out var x, out var y))
                DrawCircle(pixels, x, y, 5, originX, originY, width, height, 70, 70, 90, 110);
        }
        if (hint.Phase == CalibrationOverlayPhase.Waiting)
        {
            if (hasCenter)
                DrawRing(pixels, cx, cy, 90, 5, originX, originY, width, height, 160, 160, 180, 160);
        }
        else if (hint.Phase == CalibrationOverlayPhase.Blink)
        {
            if (hasCenter)
            {
                DrawCircle(pixels, cx, cy, 28, originX, originY, width, height, 40, 40, 48, 220);
                DrawCircle(pixels, cx, cy, 8, originX, originY, width, height, 177, 138, 244, 230);
                var blinkRadius = (int)(120 * (1 - hint.Progress));
                if (blinkRadius > 10)
                    DrawRing(pixels, cx, cy, blinkRadius, 6, originX, originY, width, height, 177, 138, 244, 210);
            }
        }
        else if (TryProject(hint.TargetX, hint.TargetY, projection, worldToEye, headPose, originX, originY, width, height,
                     out var tx, out var ty))
        {
            DrawCircle(pixels, tx, ty, 16, originX, originY, width, height, 177, 138, 244, 230);
            DrawCircle(pixels, tx, ty, 5, originX, originY, width, height, 255, 255, 255, 255);
            var radius = (int)(90 * (1 - hint.Progress));
            if (radius > 8)
                DrawRing(pixels, tx, ty, radius, 3, originX, originY, width, height, 177, 138, 244, 200);
        }

        DrawCaptions(pixels, hint, originX, originY, width, height);
    }

    private static void DrawCaptions(byte[] pixels, CalibrationOverlayHint hint, int originX, int originY, int width, int height)
    {
        var lines = OverlayPrompt.HudLines(hint);
        var y = Math.Max(24, height / 8);
        OverlayGlyphs.DrawCentered(pixels, lines[0], originX, originY, width, height, y, 4, 230, 230, 240, 240);
        for (var i = 1; i < lines.Count; i++)
        {
            OverlayGlyphs.DrawCentered(
                pixels, lines[i], originX, originY, width, height, y + 28 + (i - 1) * 22, 2, 177, 138, 244, 230);
        }
    }

    private static bool TryProject(double gazeX, double gazeY,
        in VarjoNativeMethods.VarjoMatrix projection, in VarjoNativeMethods.VarjoMatrix worldToEye,
        in VarjoNativeMethods.VarjoMatrix headPose, int originX, int originY, int width, int height,
        out int px, out int py)
    {
        var world = headPose.Transform(gazeX * 0.55, gazeY * 0.4, -TargetDistance);
        var eye = worldToEye.Transform(world.X, world.Y, world.Z, world.W);
        var clip = projection.Transform(eye.X, eye.Y, eye.Z, eye.W);
        px = 0;
        py = 0;
        if (clip.W <= 1e-6) return false;
        var ndcX = clip.X / clip.W;
        var ndcY = clip.Y / clip.W;
        if (ndcX is < -1.4 or > 1.4 || ndcY is < -1.4 or > 1.4) return false;
        px = originX + (int)Math.Round((ndcX * 0.5 + 0.5) * (width - 1));
        py = originY + (int)Math.Round((0.5 - ndcY * 0.5) * (height - 1));
        return true;
    }

    private static void DrawCircle(byte[] pixels, int cx, int cy, int radius,
        int originX, int originY, int width, int height, byte r, byte g, byte b, byte a)
    {
        var rr = radius * radius;
        for (var y = -radius; y <= radius; y++)
        {
            var py = cy + y;
            if (py < originY || py >= originY + height) continue;
            var y2 = y * y;
            for (var x = -radius; x <= radius; x++)
            {
                if (x * x + y2 > rr) continue;
                var px = cx + x;
                if (px < originX || px >= originX + width) continue;
                Blend(pixels, py * TextureSize + px, r, g, b, a);
            }
        }
    }

    private static void DrawRing(byte[] pixels, int cx, int cy, int radius, int thickness,
        int originX, int originY, int width, int height, byte r, byte g, byte b, byte a)
    {
        var outer = radius * radius;
        var inner = Math.Max(0, radius - thickness);
        inner *= inner;
        for (var y = -radius; y <= radius; y++)
        {
            var py = cy + y;
            if (py < originY || py >= originY + height) continue;
            var y2 = y * y;
            for (var x = -radius; x <= radius; x++)
            {
                var d = x * x + y2;
                if (d > outer || d < inner) continue;
                var px = cx + x;
                if (px < originX || px >= originX + width) continue;
                Blend(pixels, py * TextureSize + px, r, g, b, a);
            }
        }
    }

    internal static void BlendPixel(byte[] pixels, int pixel, byte r, byte g, byte b, byte a) =>
        Blend(pixels, pixel, r, g, b, a);

    private static void Blend(byte[] pixels, int pixel, byte r, byte g, byte b, byte a)
    {
        var i = pixel * 4;
        var src = a / 255f;
        var dst = 1 - src;
        pixels[i] = (byte)(r * src + pixels[i] * dst);
        pixels[i + 1] = (byte)(g * src + pixels[i + 1] * dst);
        pixels[i + 2] = (byte)(b * src + pixels[i + 2] * dst);
        pixels[i + 3] = (byte)Math.Clamp(pixels[i + 3] + a, 0, 255);
    }

    private static void ThrowIfError(VarjoSessionHandle session, string name)
    {
        var error = VarjoNativeMethods.GetError(session);
        if (error != 0)
            throw new InvalidOperationException($"{name}: {VarjoNativeMethods.DescribeError(error)}");
    }

    public void Dispose()
    {
        Thread? thread;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _wantRunning = false;
            _stop = true;
            _hintTimer?.Dispose();
            _hintTimer = null;
            thread = _thread;
        }
        if (thread is not null && !ReferenceEquals(Thread.CurrentThread, thread))
            thread.Join(TimeSpan.FromSeconds(2));
    }

    private static class D3D11
    {
        private static readonly Guid FactoryGuid = new("770aae78-f26f-4dba-a829-253c83d1b387");
        private const int CreateDeviceBgRa = 0x20;
        private const int SdkVersion = 7;

        [DllImport("d3d11.dll", ExactSpelling = true)]
        private static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags,
            [In] int[] featureLevels, uint featureLevelCount, uint sdkVersion,
            out IntPtr device, out int featureLevel, out IntPtr context);

        [DllImport("dxgi.dll", ExactSpelling = true)]
        private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int EnumAdapters1Delegate(IntPtr factory, uint index, out IntPtr adapter);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDesc1Delegate(IntPtr adapter, out AdapterDescription description);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GetImmediateContextDelegate(IntPtr device, out IntPtr context);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void UpdateSubresourceDelegate(IntPtr context, IntPtr resource, uint subresource,
            IntPtr box, IntPtr data, uint rowPitch, uint depthPitch);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct AdapterDescription
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Name;
            public uint VendorId, DeviceId, SubSystemId, Revision;
            public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
            public long AdapterLuid;
            public uint Flags;
        }

        public static void CreateDevice(long luid, out IntPtr device, out IntPtr context)
        {
            var adapter = FindAdapter(luid);
            var levels = new[] { 0xb100, 0xb000, 0xa100 };
            var result = D3D11CreateDevice(adapter, adapter == IntPtr.Zero ? 1 : 0, IntPtr.Zero,
                CreateDeviceBgRa, levels, (uint)levels.Length, SdkVersion, out device, out _, out var immediate);
            if (adapter != IntPtr.Zero) Marshal.Release(adapter);
            if (result < 0 || device == IntPtr.Zero)
                throw new InvalidOperationException($"D3D11CreateDevice failed (0x{result:X8}).");
            if (immediate != IntPtr.Zero)
            {
                context = immediate;
                return;
            }
            var getContext = Marshal.GetDelegateForFunctionPointer<GetImmediateContextDelegate>(
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(device), 40 * IntPtr.Size));
            getContext(device, out context);
            if (context == IntPtr.Zero)
                throw new InvalidOperationException("D3D11 immediate context is missing.");
        }

        public static void UpdateSubresource(IntPtr context, IntPtr texture, IntPtr data, int rowPitch)
        {
            var update = Marshal.GetDelegateForFunctionPointer<UpdateSubresourceDelegate>(
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(context), 48 * IntPtr.Size));
            update(context, texture, 0, IntPtr.Zero, data, (uint)rowPitch, 0);
        }

        private static IntPtr FindAdapter(long luid)
        {
            if (luid == 0) return IntPtr.Zero;
            var guid = FactoryGuid;
            if (CreateDXGIFactory1(ref guid, out var factory) < 0 || factory == IntPtr.Zero)
                return IntPtr.Zero;
            try
            {
                var enumerate = Marshal.GetDelegateForFunctionPointer<EnumAdapters1Delegate>(
                    Marshal.ReadIntPtr(Marshal.ReadIntPtr(factory), 12 * IntPtr.Size));
                for (uint i = 0; i < 32; i++)
                {
                    if (enumerate(factory, i, out var adapter) < 0) break;
                    try
                    {
                        var describe = Marshal.GetDelegateForFunctionPointer<GetDesc1Delegate>(
                            Marshal.ReadIntPtr(Marshal.ReadIntPtr(adapter), 10 * IntPtr.Size));
                        if (describe(adapter, out var details) >= 0 && details.AdapterLuid == luid)
                        {
                            Marshal.AddRef(adapter);
                            return adapter;
                        }
                    }
                    finally { Marshal.Release(adapter); }
                }
            }
            finally { Marshal.Release(factory); }
            return IntPtr.Zero;
        }
    }
}
