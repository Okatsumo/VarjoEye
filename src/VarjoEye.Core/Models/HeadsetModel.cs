namespace VarjoEye.Core.Models;

public sealed record HeadsetModel(string Id, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public static class HeadsetCatalog
{
    public static HeadsetModel Aero { get; } = new("aero", "Varjo Aero");
    public static HeadsetModel Vr1 { get; } = new("vr-1", "Varjo VR-1");
    public static HeadsetModel Vr2 { get; } = new("vr-2", "Varjo VR-2");
    public static HeadsetModel Vr3 { get; } = new("vr-3", "Varjo VR-3");
    public static HeadsetModel Xr1 { get; } = new("xr-1", "Varjo XR-1");
    public static HeadsetModel Xr3 { get; } = new("xr-3", "Varjo XR-3");
    public static HeadsetModel Xr4 { get; } = new("xr-4", "Varjo XR-4");
    public static HeadsetModel Unknown { get; } = new("unknown", "Unknown");

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
