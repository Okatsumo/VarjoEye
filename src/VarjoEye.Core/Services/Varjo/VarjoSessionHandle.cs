using Microsoft.Win32.SafeHandles;

namespace VarjoEye.Core.Services.Varjo;

internal sealed class VarjoSessionHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal VarjoSessionHandle(IntPtr handle) : base(true) => SetHandle(handle);

    protected override bool ReleaseHandle()
    {
        VarjoNativeMethods.SessionShutDown(handle);
        return true;
    }
}
