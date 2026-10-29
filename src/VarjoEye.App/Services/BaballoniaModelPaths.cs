using VarjoEye.Core.Services.Baballonia;

namespace VarjoEye.App.Services;

internal static class BaballoniaModelPaths
{
    public static BaballoniaEyeModel Create(string baseDirectory, string localAppData) => new(
        Path.Combine(baseDirectory, "Models", "Baballonia.eyeModel.onnx"),
        Path.Combine(localAppData, "VarjoEye", "Models", "Baballonia.eyeModel.user.onnx"));
}
