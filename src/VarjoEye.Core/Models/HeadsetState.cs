namespace VarjoEye.Core.Models;

public enum ConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
    Error
}

public sealed record HeadsetState(
    string Name,
    string ModelId,
    ConnectionStatus Connection,
    string RuntimeStatus,
    string EyeTrackingStatus,
    string OscOutputStatus,
    string TrackingFrequency,
    string VarjoRuntimeVersion,
    string? ErrorMessage = null,
    bool IsGazeCalibrating = false)
{
    public static HeadsetState ConnectedDemo { get; } = For(HeadsetCatalog.Aero, ConnectionStatus.Connected);

    public static HeadsetState Disconnected { get; } =
        For(HeadsetCatalog.Unknown, ConnectionStatus.Disconnected) with { Name = "No headset" };

    public static HeadsetState Connecting { get; } =
        For(HeadsetCatalog.Unknown, ConnectionStatus.Connecting) with { Name = "No headset" };

    public static HeadsetState ErrorDemo { get; } =
        For(HeadsetCatalog.Unknown, ConnectionStatus.Error) with { Name = "No headset" };

    public static HeadsetState For(HeadsetModel model, ConnectionStatus connection) => connection switch
    {
        ConnectionStatus.Connected => new(
            model.DisplayName,
            model.Id,
            ConnectionStatus.Connected,
            "Active",
            "Ready",
            "Connected",
            "200 Hz",
            "2.15.6"),
        ConnectionStatus.Connecting => new(
            model.DisplayName,
            model.Id,
            ConnectionStatus.Connecting,
            "Starting",
            "Waiting",
            "Disconnected",
            "—",
            "—"),
        ConnectionStatus.Error => new(
            model.DisplayName,
            model.Id,
            ConnectionStatus.Error,
            "Error",
            "Error",
            "Disconnected",
            "—",
            "—",
            "Varjo Runtime is not available."),
        _ => new(
            model.DisplayName,
            model.Id,
            ConnectionStatus.Disconnected,
            "Inactive",
            "Unavailable",
            "Disconnected",
            "—",
            "—")
    };

    public static HeadsetState FromVarjo(
        VarjoHeadsetInfo info,
        HeadsetModel model,
        ConnectionStatus connection,
        string eyeTrackingStatus,
        string? error) =>
        For(model, connection) with
        {
            Name = connection is ConnectionStatus.Connected or ConnectionStatus.Connecting
                ? model.DisplayName
                : "No headset",
            EyeTrackingStatus = eyeTrackingStatus,
            RuntimeStatus = connection switch
            {
                ConnectionStatus.Connected => "Active",
                ConnectionStatus.Connecting => "Starting",
                ConnectionStatus.Error => "Error",
                _ => "Inactive"
            },
            OscOutputStatus = "Disconnected",
            VarjoRuntimeVersion = string.IsNullOrWhiteSpace(info.RuntimeVersion) ? "—" : info.RuntimeVersion,
            ErrorMessage = error,
            IsGazeCalibrating = info.IsGazeCalibrating == true
        };

    public string ConnectionLabel => Connection switch
    {
        ConnectionStatus.Connected => "Connected",
        ConnectionStatus.Connecting => "Connecting",
        ConnectionStatus.Error => "Error",
        _ => "Disconnected"
    };
}
