namespace VarjoEye.Core.Services.Baballonia;

public sealed class BaballoniaEyeModel(string stockPath, string userPath)
{
    public string StockPath { get; } = Validate(stockPath);
    public string UserPath { get; } = Validate(userPath);
    public string UserDirectory => Path.GetDirectoryName(UserPath)!;
    public string ResolvedPath => HasUserModel ? UserPath : StockPath;
    public bool IsAvailable => File.Exists(ResolvedPath);
    public bool HasUserModel => File.Exists(UserPath);

    private static string Validate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Model path must be absolute.", nameof(path));
        return Path.GetFullPath(path);
    }
}
