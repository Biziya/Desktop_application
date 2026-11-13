using System.Windows;
using Frontend.Shared.Infrastructure;

namespace Frontend.Pages.Monitoring;

/// <summary>Monitoring.yaml: how often the CT008 database is read and when a detector counts as stale.</summary>
public sealed class MonitoringSettings
{
    public const string File = "Pages/Monitoring/Monitoring.yaml";

    /// <summary>Resource key under which MainViewModel finds these settings.</summary>
    public const string Resource = "MonitoringSettings";

    public static MonitoringSettings Default { get; } = new()
    {
        PollIntervalMs = 1000,
        StaleAfterMs = 5000,
        RecentReadingsShown = 50,
        SamplesShown = 100
    };

    public required int PollIntervalMs { get; init; }

    public required int StaleAfterMs { get; init; }

    public required int RecentReadingsShown { get; init; }

    public required int SamplesShown { get; init; }

    public static MonitoringSettings Load(string baseDirectory)
    {
        var settings = YamlSettings.Load<MonitoringSettings>(baseDirectory, File);

        foreach (var (key, value) in new[]
                 {
                     ("pollIntervalMs", settings.PollIntervalMs), ("staleAfterMs", settings.StaleAfterMs),
                     ("recentReadingsShown", settings.RecentReadingsShown), ("samplesShown", settings.SamplesShown)
                 })
        {
            if (value < 1)
            {
                throw YamlSettings.Invalid(File, key, $"must be 1 or more (is {value}).");
            }
        }

        return settings;
    }

    public void AddTo(ResourceDictionary resources) => resources.Add(Resource, this);
}
