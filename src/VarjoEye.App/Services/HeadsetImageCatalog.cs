namespace VarjoEye.App.Services;

internal static class HeadsetImageCatalog
{
    public static string GetImageKey(string? modelId) => modelId switch
    {
        "aero" => "headset-aero.png",
        "vr-1" => "headset-vr-1.png",
        "vr-2" => "headset-vr-2.png",
        "vr-3" => "headset-vr-3.png",
        "xr-1" => "headset-xr-1.png",
        "xr-3" => "headset-xr-3.png",
        "xr-4" => "headset-xr-4.png",
        _ => "headset-unknown.png"
    };
}
