using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace VarjoEye.Core.Services.Varjo;

internal static class VarjoNativeMethods
{
    private const string LibraryName = "VarjoLib";
    private const string CompositorDirectory = "varjo-compositor";
    private const string LibraryFileName = "VarjoLib.dll";

    static VarjoNativeMethods() =>
        NativeLibrary.SetDllImportResolver(typeof(VarjoNativeMethods).Assembly, ResolveLibrary);

    internal static string? LoadedLibraryPath { get; private set; }

    private static IntPtr ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.OrdinalIgnoreCase))
        {
            return IntPtr.Zero;
        }

        foreach (var path in GetInstalledLibraryPaths())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var handle = NativeLibrary.Load(path);
                LoadedLibraryPath = path;
                return handle;
            }
            catch (DllNotFoundException)
            {
            }
            catch (BadImageFormatException)
            {
            }
        }

        return IntPtr.Zero;
    }

    private static IEnumerable<string> GetInstalledLibraryPaths()
    {
        var installLocation = GetVarjoBaseInstallLocation();
        if (!string.IsNullOrWhiteSpace(installLocation))
        {
            yield return Path.Combine(installLocation, CompositorDirectory, LibraryFileName);
        }

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Varjo",
            CompositorDirectory,
            LibraryFileName);
    }

    private static string? GetVarjoBaseInstallLocation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using var uninstall = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null)
            {
                return null;
            }

            foreach (var subKeyName in uninstall.GetSubKeyNames())
            {
                using var application = uninstall.OpenSubKey(subKeyName);
                if (application?.GetValue("DisplayName") as string != "Varjo Base")
                {
                    continue;
                }

                return application.GetValue("InstallLocation") as string;
            }
        }
        catch (System.Security.SecurityException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }

    internal const long HmdConnectedPropertyKey = 0xE001;
    internal const long HmdProductNamePropertyKey = 0xE002;
    internal const long GazeCalibratingPropertyKey = 0xA000;
    internal const long GazeCalibratedPropertyKey = 0xA001;
    internal const long UserPresencePropertyKey = 0x2000;
    internal const long GazeAllowedPropertyKey = 0xA003;
    internal const long GazeCalibrationQualityLeftPropertyKey = 0xA004;
    internal const long GazeCalibrationQualityRightPropertyKey = 0xA005;
    internal const long EyeCameraStreamType = 3;
    internal const long CpuBufferType = 1;
    internal const long InvalidId = -1;
    internal const int StreamFrameEyeCameraTimestampOffset = 168;

    [StructLayout(LayoutKind.Sequential)]
    internal struct GazeRay
    {
        public double OriginX, OriginY, OriginZ;
        public double DirectionX, DirectionY, DirectionZ;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Gaze
    {
        public GazeRay LeftEye, RightEye, Combined;
        public double FocusDistance, Stability;
        public long CaptureTime, LeftStatus, RightStatus, Status, FrameNumber;
        public double DeprecatedLeftPupilSize, DeprecatedRightPupilSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EyeMeasurements
    {
        public long FrameNumber, CaptureTime;
        public float InterPupillaryDistanceMm;
        public float LeftPupilIrisRatio, RightPupilIrisRatio;
        public float LeftPupilDiameterMm, RightPupilDiameterMm;
        public float LeftIrisDiameterMm, RightIrisDiameterMm;
        public float LeftOpenness, RightOpenness;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GazeParameter
    {
        public IntPtr Key;
        public IntPtr Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct StreamConfig
    {
        public long StreamId;
        public ulong ChannelFlags;
        public long StreamType;
        public long BufferType;
        public long Format;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public double[] StreamTransform;
        public int FrameRate;
        public int Width;
        public int Height;
        public int RowStride;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BufferMetadata
    {
        public long Format;
        public long Type;
        public int ByteSize;
        public int RowStride;
        public int Width;
        public int Height;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void FrameListener(IntPtr frame, IntPtr session, IntPtr userData);

    [DllImport(LibraryName, EntryPoint = "varjo_GetVersionString", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr GetVersionStringPtr();

    internal static string? GetVersionString()
    {
        var pointer = GetVersionStringPtr();
        return pointer == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(pointer);
    }

    [DllImport(LibraryName, EntryPoint = "varjo_IsAvailable", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int IsAvailable();

    [DllImport(LibraryName, EntryPoint = "varjo_SessionInit", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SessionInit();

    [DllImport(LibraryName, EntryPoint = "varjo_SessionShutDown", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SessionShutDown(IntPtr session);

    [DllImport(LibraryName, EntryPoint = "varjo_IsGazeAllowed", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int IsGazeAllowed(VarjoSessionHandle session);

    [DllImport(LibraryName, EntryPoint = "varjo_GazeInitWithParameters", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void GazeInitWithParameters(
        VarjoSessionHandle session,
        [In] GazeParameter[] parameters,
        int parameterCount);

    [DllImport(LibraryName, EntryPoint = "varjo_GetGazeData", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int GetGazeData(VarjoSessionHandle session, out Gaze gaze, out EyeMeasurements measurements);

    [DllImport(LibraryName, EntryPoint = "varjo_GetGazeDataArray", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int GetGazeDataArray(VarjoSessionHandle session,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] Gaze[] gaze,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] EyeMeasurements[] measurements, int capacity);

    [DllImport(LibraryName, EntryPoint = "varjo_RequestGazeCalibration", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void RequestGazeCalibration(VarjoSessionHandle session);

    [DllImport(LibraryName, EntryPoint = "varjo_GetCurrentTime", CallingConvention = CallingConvention.Cdecl)]
    internal static extern long GetCurrentTime(VarjoSessionHandle session);

    [DllImport(LibraryName, EntryPoint = "varjo_GetError", CallingConvention = CallingConvention.Cdecl)]
    internal static extern long GetError(VarjoSessionHandle session);

    [DllImport(LibraryName, EntryPoint = "varjo_SyncProperties", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SyncProperties(VarjoSessionHandle session);

    [DllImport(LibraryName, EntryPoint = "varjo_HasProperty", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int HasProperty(VarjoSessionHandle session, long propertyKey);

    [DllImport(LibraryName, EntryPoint = "varjo_GetPropertyBool", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int GetPropertyBool(VarjoSessionHandle session, long propertyKey);

    [DllImport(LibraryName, EntryPoint = "varjo_GetPropertyInt", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int GetPropertyInt(VarjoSessionHandle session, long propertyKey);

    [DllImport(LibraryName, EntryPoint = "varjo_GetPropertyStringSize", CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint GetPropertyStringSize(VarjoSessionHandle session, long propertyKey);

    [DllImport(LibraryName, EntryPoint = "varjo_GetPropertyString", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void GetPropertyString(VarjoSessionHandle session, long propertyKey, byte[] buffer, uint maxSize);

    [DllImport(LibraryName, EntryPoint = "varjo_GetErrorDesc", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr GetErrorDesc(long error);

    internal static string DescribeError(long error) =>
        Marshal.PtrToStringUTF8(GetErrorDesc(error)) ?? $"Varjo error {error}.";

    [DllImport(LibraryName, EntryPoint = "varjo_GetDataStreamConfigCount", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int GetDataStreamConfigCount(VarjoSessionHandle session);

    [DllImport(LibraryName, EntryPoint = "varjo_GetDataStreamConfigs", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void GetDataStreamConfigs(VarjoSessionHandle session, IntPtr configs, int maxSize);

    [DllImport(LibraryName, EntryPoint = "varjo_StartDataStream", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void StartDataStream(
        VarjoSessionHandle session,
        long id,
        ulong channels,
        FrameListener callback,
        IntPtr userData);

    [DllImport(LibraryName, EntryPoint = "varjo_StopDataStream", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void StopDataStream(VarjoSessionHandle session, long id);

    [DllImport(LibraryName, EntryPoint = "varjo_GetBufferId", CallingConvention = CallingConvention.Cdecl)]
    internal static extern long GetBufferId(IntPtr session, long id, long frameNumber, long channelIndex);

    [DllImport(LibraryName, EntryPoint = "varjo_LockDataStreamBuffer", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void LockDataStreamBuffer(IntPtr session, long id);

    [DllImport(LibraryName, EntryPoint = "varjo_UnlockDataStreamBuffer", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void UnlockDataStreamBuffer(IntPtr session, long id);

    [DllImport(LibraryName, EntryPoint = "varjo_GetBufferMetadata", CallingConvention = CallingConvention.Cdecl)]
    internal static extern BufferMetadata GetBufferMetadata(IntPtr session, long id);

    [DllImport(LibraryName, EntryPoint = "varjo_GetBufferCPUData", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr GetBufferCPUData(IntPtr session, long id);

    [DllImport(LibraryName, EntryPoint = "varjo_SessionSetPriority", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SessionSetPriority(VarjoSessionHandle session, int priority);

    [DllImport(LibraryName, EntryPoint = "varjo_CreateFrameInfo", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr CreateFrameInfo(VarjoSessionHandle session);

    [DllImport(LibraryName, EntryPoint = "varjo_FreeFrameInfo", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void FreeFrameInfo(IntPtr frameInfo);

    [DllImport(LibraryName, EntryPoint = "varjo_GetViewCount", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int GetViewCount(VarjoSessionHandle session);

    [DllImport(LibraryName, EntryPoint = "varjo_GetViewDescription", CallingConvention = CallingConvention.Cdecl)]
    internal static extern ViewDescription GetViewDescription(VarjoSessionHandle session, int viewIndex);

    [DllImport(LibraryName, EntryPoint = "varjo_WaitSync", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void WaitSync(VarjoSessionHandle session, IntPtr frameInfo);

    [DllImport(LibraryName, EntryPoint = "varjo_FrameGetPose", CallingConvention = CallingConvention.Cdecl)]
    internal static extern VarjoMatrix FrameGetPose(VarjoSessionHandle session, long poseType);

    [DllImport(LibraryName, EntryPoint = "varjo_BeginFrameWithLayers", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void BeginFrameWithLayers(VarjoSessionHandle session);

    [DllImport(LibraryName, EntryPoint = "varjo_EndFrameWithLayers", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void EndFrameWithLayers(VarjoSessionHandle session, in SubmitInfoLayers submitInfo);

    [DllImport(LibraryName, EntryPoint = "varjo_D3D11CreateSwapChain", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr D3D11CreateSwapChain(VarjoSessionHandle session, IntPtr device, in SwapChainConfig2 config);

    [DllImport(LibraryName, EntryPoint = "varjo_GetSwapChainImage", CallingConvention = CallingConvention.Cdecl)]
    internal static extern VarjoTexture GetSwapChainImage(IntPtr swapChain, int index);

    [DllImport(LibraryName, EntryPoint = "varjo_AcquireSwapChainImage", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void AcquireSwapChainImage(IntPtr swapChain, out int index);

    [DllImport(LibraryName, EntryPoint = "varjo_ReleaseSwapChainImage", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ReleaseSwapChainImage(IntPtr swapChain);

    [DllImport(LibraryName, EntryPoint = "varjo_FreeSwapChain", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void FreeSwapChain(IntPtr swapChain);

    [DllImport(LibraryName, EntryPoint = "varjo_ToD3D11Texture", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ToD3D11Texture(VarjoTexture texture);

    [DllImport(LibraryName, EntryPoint = "varjo_D3D11GetLuid", CallingConvention = CallingConvention.Cdecl)]
    internal static extern AdapterLuid D3D11GetLuid(VarjoSessionHandle session);

    internal const long TextureFormatRgbaSrgb = 1;
    internal const long LayerMultiProjType = 1;
    internal const long LayerFlagAlphaBlend = 2;
    internal const long SpaceLocal = 0;
    internal const long DisplayTypeContext = 2;
    internal const long EyeRight = 2;
    internal const long PoseTypeCenter = 2;
    internal const int OverlaySessionPriority = 9999;
    internal const int ViewInfoSize = 272;
    internal const int ViewInfoViewOffset = 128;
    internal const int ViewInfoEnabledOffset = 264;

    [StructLayout(LayoutKind.Sequential)]
    internal struct AdapterLuid
    {
        public uint Low;
        public int High;

        public long ToInt64() => ((long)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VarjoTexture
    {
        public long R0, R1, R2, R3;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SwapChainConfig2
    {
        public long TextureFormat;
        public int NumberOfTextures;
        public int TextureWidth;
        public int TextureHeight;
        public int TextureArraySize;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LayerHeader
    {
        public long Type;
        public long Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SubmitInfoLayers
    {
        public long FrameNumber;
        public long Reserved;
        public int LayerCount;
        public int Padding;
        public IntPtr Layers;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LayerMultiProj
    {
        public LayerHeader Header;
        public long Space;
        public int ViewCount;
        public int Padding;
        public IntPtr Views;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SwapChainViewport
    {
        public IntPtr SwapChain;
        public int X, Y, Width, Height, ArrayIndex, Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VarjoMatrix
    {
        public double M00, M10, M20, M30;
        public double M01, M11, M21, M31;
        public double M02, M12, M22, M32;
        public double M03, M13, M23, M33;

        public static VarjoMatrix FromDoubles(double[] values) => new()
        {
            M00 = values[0], M10 = values[1], M20 = values[2], M30 = values[3],
            M01 = values[4], M11 = values[5], M21 = values[6], M31 = values[7],
            M02 = values[8], M12 = values[9], M22 = values[10], M32 = values[11],
            M03 = values[12], M13 = values[13], M23 = values[14], M33 = values[15],
        };

        public readonly (double X, double Y, double Z, double W) Transform(double x, double y, double z, double w = 1) =>
            (
                M00 * x + M01 * y + M02 * z + M03 * w,
                M10 * x + M11 * y + M12 * z + M13 * w,
                M20 * x + M21 * y + M22 * z + M23 * w,
                M30 * x + M31 * y + M32 * z + M33 * w);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LayerMultiProjView
    {
        public IntPtr Extension;
        public VarjoMatrix Projection;
        public VarjoMatrix View;
        public SwapChainViewport Viewport;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FrameInfo
    {
        public IntPtr Views;
        public long DisplayTime;
        public long FrameNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ViewDescription
    {
        public int Width;
        public int Height;
        public long Display;
        public long Eye;
    }
}
