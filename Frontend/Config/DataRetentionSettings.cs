using System.Windows;
using Frontend.Shared.Infrastructure;

namespace Frontend.Config;

/// <summary>Config/data-retention.yaml: how long measurements stay in measurements.db.</summary>
public sealed class DataRetentionSettings
{
    public const string File = "Config/data-retention.yaml";

    /// <summary>Resource key under which MainViewModel finds these settings.</summary>
    public const string Resource = "DataRetentionSettings";

    public required bool Enabled { get; init; }

    public required int RetentionDays { get; init; }

    public required int CheckIntervalHours { get; init; }

    public TimeSpan RetentionPeriod => TimeSpan.FromDays(RetentionDays);

    public TimeSpan CheckInterval => TimeSpan.FromHours(CheckIntervalHours);

    public static DataRetentionSettings Load(string baseDirectory)
    {
        var settings = YamlSettings.Load<DataRetentionSettings>(baseDirectory, File);

        if (settings.RetentionDays < 1)
        {
            throw YamlSettings.Invalid(File, "retentionDays", $"must be 1 or more (is {settings.RetentionDays}).");
        }

        if (settings.CheckIntervalHours < 1)
        {
            throw YamlSettings.Invalid(File, "checkIntervalHours", $"must be 1 or more (is {settings.CheckIntervalHours}).");
        }

        return settings;
    }

    public void AddTo(ResourceDictionary resources) => resources.Add(Resource, this);
}
