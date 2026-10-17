using System.IO;
using System.Windows;
using Frontend.Pages.CoreInformation.DeviceStatus;
using Frontend.Pages.CoreInformation.DrillholeInformation;
using Frontend.Pages.Measurement.CapturePanel;
using Frontend.Pages.Measurement.Table;
using Frontend.Shared.Infrastructure;

namespace Frontend.Pages;

/// <summary>
/// Reads the four section YAML files (next to the executable) once at start-up and adds their values to
/// <paramref name="resources"/>, where the section views use them as {StaticResource …}. Edits take effect after a
/// restart. Throws <see cref="InvalidDataException"/> naming the file and key of the first problem.
/// </summary>
public static class PageSettings
{
    public static void AddTo(ResourceDictionary resources, string baseDirectory)
    {
        var settings = new ResourceDictionary();
        YamlSettings.Load<DrillholeInformationSettings>(baseDirectory, DrillholeInformationSettings.File).AddTo(settings);
        YamlSettings.Load<DeviceStatusSettings>(baseDirectory, DeviceStatusSettings.File).AddTo(settings);
        YamlSettings.Load<MeasurementTableSettings>(baseDirectory, MeasurementTableSettings.File).AddTo(settings);
        YamlSettings.Load<CapturePanelSettings>(baseDirectory, CapturePanelSettings.File).AddTo(settings);
        resources.MergedDictionaries.Add(settings);
    }
}
