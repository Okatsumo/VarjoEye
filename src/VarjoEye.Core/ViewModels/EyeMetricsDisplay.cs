using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using VarjoEye.Core.Models;

namespace VarjoEye.Core.ViewModels;

public partial class EyeMetricsDisplay : ObservableObject
{
    [ObservableProperty] private double _openness;
    [ObservableProperty] private string _opennessLabel = "—";
    [ObservableProperty] private string _pupilDiameter = "—";
    [ObservableProperty] private string _irisDiameter = "—";
    [ObservableProperty] private string _pupilIrisRatio = "—";
    [ObservableProperty] private string _legacyPupilSize = "—";
    [ObservableProperty] private string _calibrationQuality = "—";
    [ObservableProperty] private string _gazeOrigin = "—";
    [ObservableProperty] private string _gazeDirection = "—";
    [ObservableProperty] private string _babbleOpenness = "—";
    [ObservableProperty] private string _babbleGazeX = "—";
    [ObservableProperty] private string _babbleGazeY = "—";
    [ObservableProperty] private string _combinedOpenness = "—";
    [ObservableProperty] private string _combinedGazeX = "—";
    [ObservableProperty] private string _combinedGazeY = "—";

    public void Update(EyeSourceState eye, bool hasData)
    {
        Openness = hasData ? Math.Clamp(eye.Varjo.Openness, 0, 1) : 0;
        OpennessLabel = Format(hasData, eye.Varjo.Openness);
        PupilDiameter = FormatMm(hasData, eye.Varjo.PupilDiameterMm);
        IrisDiameter = FormatMm(hasData, eye.Varjo.IrisDiameterMm);
        PupilIrisRatio = Format(hasData, eye.Varjo.PupilIrisRatio);
        LegacyPupilSize = Format(hasData, eye.Varjo.LegacyPupilSize);
        CalibrationQuality = hasData ? eye.Varjo.CalibrationQuality : "—";
        GazeOrigin = hasData ? eye.Varjo.GazeOriginMeters.Display : "—";
        GazeDirection = hasData ? eye.Varjo.GazeDirection.Display : "—";
        BabbleOpenness = Format(eye.Babble.HasSample && eye.Babble.IsOpennessValid, eye.Babble.Openness);
        BabbleGazeX = Format(eye.Babble.HasSample && eye.Babble.IsGazeValid, eye.Babble.GazeX);
        BabbleGazeY = Format(eye.Babble.HasSample && eye.Babble.IsGazeValid, eye.Babble.GazeY);
        CombinedOpenness = Format(hasData, eye.Combined.Openness);
        CombinedGazeX = Format(hasData, eye.Combined.GazeX);
        CombinedGazeY = Format(hasData, eye.Combined.GazeY);
    }

    private static string Format(bool hasData, double value) =>
        hasData ? value.ToString("0.00", CultureInfo.InvariantCulture) : "—";

    private static string FormatMm(bool hasData, double value) =>
        hasData ? $"{value.ToString("0.00", CultureInfo.InvariantCulture)} mm" : "—";
}
