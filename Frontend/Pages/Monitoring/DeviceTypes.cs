namespace Frontend.Pages.Monitoring;

/// <summary>Device type numbers reported by the detectors, named as the CT008 application names them.</summary>
public static class DeviceTypes
{
    public const int Manometer = 3;

    private static readonly Dictionary<int, string> Names = new()
    {
        [Manometer] = "Manometer",
        [10] = "Contamination Detector",
        [14] = "MicroR Meter",
        [15] = "Nano Nuke",
        [16] = "4M Meter",
        [19] = "Neutron Detector",
        [20] = "DRD"
    };

    public static string Name(int? type) =>
        type is null ? "Unknown" : Names.TryGetValue(type.Value, out var name) ? name : $"Type {type}";
}
