using VarjoEye.Core.Models;
using VarjoEye.Core.Services;
using Xunit;

namespace VarjoEye.Tests.Services;

public sealed class FileLogServiceTests
{
    [Fact]
    public void WritesNewFilePerInstance()
    {
        var directory = CreateTempDirectory();
        try
        {
            using var first = new FileLogService(directory);
            first.Add(LogLevel.Information, "one");
            using var second = new FileLogService(directory);
            second.Add(LogLevel.Warning, "two");

            Assert.NotEqual(first.CurrentFilePath, second.CurrentFilePath);
            Assert.True(File.Exists(first.CurrentFilePath));
            Assert.True(File.Exists(second.CurrentFilePath));
            Assert.Contains("one", ReadShared(first.CurrentFilePath));
            Assert.Contains("two", ReadShared(second.CurrentFilePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Clear_DoesNotDeleteFile()
    {
        var directory = CreateTempDirectory();
        try
        {
            using var logs = new FileLogService(directory);
            logs.Add(LogLevel.Error, "keep");
            logs.Clear();

            Assert.Empty(logs.Entries);
            Assert.Contains("keep", ReadShared(logs.CurrentFilePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "VarjoEyeLogs-" + Guid.NewGuid().ToString("N"))).FullName;

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
