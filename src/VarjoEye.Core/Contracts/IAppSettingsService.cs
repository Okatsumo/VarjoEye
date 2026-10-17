using VarjoEye.Core.Models;

namespace VarjoEye.Core.Contracts;

public interface IAppSettingsService
{
    AppSettings Current { get; }

    event EventHandler? Changed;

    void NotifyChanged();
}
