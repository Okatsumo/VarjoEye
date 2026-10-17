namespace VarjoEye.Core.Models;

public sealed record HeadsetModel(string Id, string DisplayName, string ImageAssetKey)
{
    public override string ToString() => DisplayName;
}

public static class HeadsetCatalog
{
    public static HeadsetModel Aero { get; } = new("aero", "Varjo Aero", "headset-aero.png");
    public static HeadsetModel Vr1 { get; } = new("vr-1", "Varjo VR-1", "headset-vr-1.png");
    public static HeadsetModel Vr2 { get; } = new("vr-2", "Varjo VR-2", "headset-vr-2.png");
    public static HeadsetModel Vr3 { get; } = new("vr-3", "Varjo VR-3", "headset-vr-3.png");
    public static HeadsetModel Xr1 { get; } = new("xr-1", "Varjo XR-1", "headset-xr-1.png");
    public static HeadsetModel Xr3 { get; } = new("xr-3", "Varjo XR-3", "headset-xr-3.png");
    public static HeadsetModel Xr4 { get; } = new("xr-4", "Varjo XR-4", "headset-xr-4.png");
    public static HeadsetModel Unknown { get; } = new("unknown", "Unknown", "headset-unknown.png");

    public static IReadOnlyList<HeadsetModel> Known { get; } =
    [
        Xr4, Xr3, Xr1, Vr3, Vr2, Vr1, Aero
    ];

    public static IReadOnlyList<HeadsetModel> All { get; } =
    [
        Aero, Vr1, Vr2, Vr3, Xr1, Xr3, Xr4, Unknown
    ];

    public static HeadsetModel Resolve(string? productName)
    {
        if (string.IsNullOrWhiteSpace(productName))
        {
            return Unknown;
        }

        var name = productName.Trim();
        foreach (var model in Known)
        {
            if (name.Contains(model.DisplayName, StringComparison.OrdinalIgnoreCase) ||
                ContainsToken(name, model.Id) ||
                ContainsToken(name, model.Id.Replace("-", "", StringComparison.Ordinal)))
            {
                return model;
            }
        }

        return Unknown with { DisplayName = name };
    }

    private static bool ContainsToken(string name, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        return name.Contains(token, StringComparison.OrdinalIgnoreCase);
    }
}
