using VarjoEye.Core.Services.Baballonia;

namespace VarjoEye.Tests;

internal static class TestModelPaths
{
    public static BaballoniaEyeModel Create() => new(
        Path.Combine(AppContext.BaseDirectory, "Models", "Baballonia.eyeModel.onnx"),
        Path.Combine(Path.GetTempPath(), "VarjoEye.Tests", Guid.NewGuid().ToString("N"), "user.onnx"));
}
