using System.Runtime.InteropServices;
using VarjoEye.Core.Models;

namespace VarjoEye.Core.Services.Baballonia;

public static class DxgiGpuDiscovery
{
    private static readonly Guid FactoryGuid = new("770aae78-f26f-4dba-a829-253c83d1b387");
    private const int DxgiErrorNotFound = unchecked((int)0x887A0002);
    private const uint SoftwareAdapterFlag = 2;

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1Delegate(IntPtr factory, uint adapterIndex, out IntPtr adapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesc1Delegate(IntPtr adapter, out AdapterDescription description);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Name;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSystemId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public long AdapterLuid;
        public uint Flags;
    }

    public static IReadOnlyList<InferenceDevice> GetDevices()
    {
        var devices = new List<InferenceDevice> { InferenceDevice.Cpu };
        if (!OperatingSystem.IsWindows())
        {
            return devices;
        }

        var factory = IntPtr.Zero;
        try
        {
            var guid = FactoryGuid;
            if (CreateDXGIFactory1(ref guid, out factory) < 0 || factory == IntPtr.Zero)
            {
                return devices;
            }

            var enumerate = Marshal.GetDelegateForFunctionPointer<EnumAdapters1Delegate>(
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(factory), 12 * IntPtr.Size));
            for (uint index = 0; index < 32; index++)
            {
                var result = enumerate(factory, index, out var adapter);
                if (result == DxgiErrorNotFound)
                {
                    break;
                }

                if (result < 0)
                {
                    break;
                }

                try
                {
                    var describe = Marshal.GetDelegateForFunctionPointer<GetDesc1Delegate>(
                        Marshal.ReadIntPtr(Marshal.ReadIntPtr(adapter), 10 * IntPtr.Size));
                    if (describe(adapter, out var details) >= 0 &&
                        (details.Flags & SoftwareAdapterFlag) == 0)
                    {
                        devices.Add(new InferenceDevice((int)index, $"GPU {index} · {details.Name.TrimEnd('\0')}"));
                    }
                }
                finally
                {
                    Marshal.Release(adapter);
                }
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
        catch (COMException)
        {
        }
        finally
        {
            if (factory != IntPtr.Zero)
            {
                Marshal.Release(factory);
            }
        }

        return devices;
    }
}
