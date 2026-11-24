using System.Text.RegularExpressions;
using System.Windows;
using Frontend.Shared.Infrastructure;

namespace Frontend.Config;

/// <summary>
/// Config/application.yaml. Also read by Frontend.csproj and build-installer.ps1, so the window title, assembly and
/// Velopack package always show the same version.
/// </summary>
public sealed partial class ApplicationSettings
{
    public const string File = "Config/application.yaml";

    public required ApplicationInfo Application { get; init; }

    public static ApplicationSettings Load(string baseDirectory)
    {
        var settings = YamlSettings.Load<ApplicationSettings>(baseDirectory, File);
        var application = settings.Application;

        // Velopack accepts letters, digits, '.', '-' and '_' in the package id and a SemVer 2 version.
        if (!PackageId().IsMatch(application.Id))
        {
            throw YamlSettings.Invalid(File, "application.id", "use only letters, digits, '.', '-' and '_' (for example CT007.MeasurementStudio).");
        }

        if (string.IsNullOrWhiteSpace(application.Name))
        {
            throw YamlSettings.Invalid(File, "application.name", "a value is required.");
        }

        if (!SemanticVersion().IsMatch(application.Version))
        {
            throw YamlSettings.Invalid(File, "application.version", $"'{application.Version}' is not a valid version (for example 1.2.1.0).");
        }

        return settings;
    }

    public void AddTo(ResourceDictionary resources)
    {
        resources.Add("ApplicationName", Application.Name);
        resources.Add("ApplicationVersion", Application.Version);
        resources.Add("ApplicationTitle", $"{Application.Name} {Application.Version}");
        resources.Add("ApplicationVersionLabel", $"Version {Application.Version}");
    }

    public sealed class ApplicationInfo
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Version { get; init; }
        public required string Description { get; init; }
    }

    [GeneratedRegex(@"^[A-Za-z0-9._-]+$")]
    private static partial Regex PackageId();

    // major.minor.patch with an optional .build number, -prerelease and +metadata (1.2.1.0, 1.2.1, 1.3.0-beta.1).
    [GeneratedRegex(@"^\d+\.\d+\.\d+(\.\d+)?(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$")]
    private static partial Regex SemanticVersion();
}
