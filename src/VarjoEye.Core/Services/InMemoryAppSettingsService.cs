using VarjoEye.Core.Contracts;
using VarjoEye.Core.Models;

namespace VarjoEye.Core.Services;

public sealed class InMemoryAppSettingsService : IAppSettingsService
{
    public AppSettings Current { get; } = new();

    public event EventHandler? Changed;

    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
