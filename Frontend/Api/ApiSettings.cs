using System.IO;
using System.Reflection;

namespace Frontend.Api;

/// <summary>
/// Backend settings, read once at start-up from exactly one .env file (docs/API_CONFIGURATION.md). The build decides
/// which one (AppEnvironment in Frontend.csproj: Debug builds local, Release builds and the installer production):
/// - local: Frontend\.env.local of the source checkout the app runs from (dotnet run / dotnet watch run).
/// - production: %LocalAppData%\CT007_Measurement_Studio\Config\.env.production. On first start it is created from
///   Config\.env.production.template shipped with the app; an existing file is never overwritten, so the address
///   stays editable and survives updates.
/// API_BASE_URL is required; the two timings default to 5 s. Every problem stops the app with a message instead of
/// silently pointing it at another backend: there is no fallback from one file or environment to another.
/// </summary>
public sealed record ApiSettings(string BaseUrl, TimeSpan RequestTimeout, TimeSpan StatusInterval)
{
    public const string EnvironmentKey = "APP_ENV";
    public const string BaseUrlKey = "API_BASE_URL";
    public const string TimeoutKey = "API_TIMEOUT_SECONDS";
    public const string RetryIntervalKey = "API_RETRY_INTERVAL_SECONDS";

    /// <summary>The production template's path relative to the executable (Frontend.csproj copies it there).</summary>
    public const string ProductionTemplate = @"Config\.env.production.template";

    // Optional values only; the backend address never has a default.
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultStatusInterval = TimeSpan.FromSeconds(5);

    /// <summary>The settings the app uses: the .env file of the build's environment.</summary>
    /// <exception cref="InvalidDataException">The configuration is missing or invalid; the message says what to fix.</exception>
    public static ApiSettings LoadConfigured()
    {
        var environment = BuildEnvironment;

        return environment switch
        {
            "local" => Load("local", LocalFile(AppContext.BaseDirectory)),
            "production" => Load("production", InitializeProductionFile(ProductionFile, Path.Combine(AppContext.BaseDirectory, ProductionTemplate))),
            _ => throw new InvalidDataException($"This build has AppEnvironment '{environment}'; it must be 'local' or 'production'. Rebuild the application.")
        };
    }

    /// <summary>AppEnvironment from Frontend.csproj, compiled into the assembly.</summary>
    public static string? BuildEnvironment => typeof(ApiSettings).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == "AppEnvironment")?.Value;

    /// <summary>%LocalAppData%\CT007_Measurement_Studio\Config\.env.production: per user, beside measurements.db, kept by updates.</summary>
    public static string ProductionFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CT007_Measurement_Studio", "Config", ".env.production");

    /// <summary>.env.local in the folder holding Frontend.csproj above <paramref name="baseDirectory"/> (bin\Debug\...).</summary>
    /// <exception cref="InvalidDataException">The app does not run from a source checkout.</exception>
    public static string LocalFile(string baseDirectory)
    {
        for (var directory = new DirectoryInfo(baseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Frontend.csproj")))
            {
                return Path.Combine(directory.FullName, ".env.local");
            }
        }

        throw new InvalidDataException(
            $"This is a development (Debug) build: it reads Frontend\\.env.local and must run from the source checkout " +
            $"(dotnet watch run in Frontend). Installed copies are built with build-installer.ps1.");
    }

    /// <summary>
    /// Copies <paramref name="template"/> to <paramref name="file"/> when the file does not exist yet (first start).
    /// An existing file is left untouched. Returns <paramref name="file"/>.
    /// </summary>
    /// <exception cref="InvalidDataException">The file is missing and cannot be created from the template.</exception>
    public static string InitializeProductionFile(string file, string template)
    {
        if (File.Exists(file) || !File.Exists(template))
        {
            return file;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.Copy(template, file, overwrite: false);
        }
        catch (IOException) when (File.Exists(file))
        {
            // Another instance created it first; use that one.
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"{file} could not be created from {template} ({error.Message}).", error);
        }

        return file;
    }

    /// <summary>Reads <paramref name="path"/>, the .env file of <paramref name="environment"/>.</summary>
    /// <exception cref="InvalidDataException">The file is missing or a line or value is invalid, or API_BASE_URL is not set.</exception>
    public static ApiSettings Load(string environment, string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidDataException(environment == "local"
                ? $"{path} was not found. Create it from Frontend\\.env.example and set {BaseUrlKey} to the development backend."
                : $"{path} was not found, and this installation has no configuration template to create it from.\n\n" +
                  $"An administrator can create the file (see .env.example) with {BaseUrlKey} set to the backend address, " +
                  $"or rebuild the installer with build-installer.ps1 after filling in Frontend\\.env.production.");
        }

        var file = ReadEnvFile(path);

        // The build chose the file; an APP_ENV line inside it may only confirm that choice.
        if (file.TryGetValue(EnvironmentKey, out var declared) && declared != environment)
        {
            throw new InvalidDataException($"{path}: {EnvironmentKey}={declared}, but this file is the {environment} configuration. Set {EnvironmentKey}={environment} or remove the line.");
        }

        if (!file.TryGetValue(BaseUrlKey, out var baseUrl) || baseUrl.Length == 0)
        {
            throw new InvalidDataException($"{path} does not set {BaseUrlKey}. Add the backend address, for example {BaseUrlKey}=https://ct007-api.example.com.");
        }

        return new ApiSettings(
            CheckedUrl(baseUrl, path),
            file.TryGetValue(TimeoutKey, out var timeout) && timeout.Length > 0 ? CheckedSeconds(timeout, TimeoutKey, path) : DefaultTimeout,
            file.TryGetValue(RetryIntervalKey, out var interval) && interval.Length > 0 ? CheckedSeconds(interval, RetryIntervalKey, path) : DefaultStatusInterval);
    }

    // KEY=value lines; blank lines and # comments are skipped, surrounding quotes removed.
    private static Dictionary<string, string> ReadEnvFile(string path)
    {
        string[] lines;

        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"{path}: the file cannot be read ({error.Message}).", error);
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=');

            if (separator <= 0)
            {
                throw new InvalidDataException($"{path}, line {i + 1}: expected KEY=value.");
            }

            values[line[..separator].Trim()] = line[(separator + 1)..].Trim().Trim('"', '\'');
        }

        return values;
    }

    // The URL itself is not repeated in the message: it could carry credentials.
    private static string CheckedUrl(string value, string source) =>
        Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme is "http" or "https" && url.UserInfo.Length == 0
            ? value
            : throw new InvalidDataException(
                $"{source}: {BaseUrlKey} must be an absolute http:// or https:// URL without a user name or password (for example https://ct007-api.example.com).");

    private static TimeSpan CheckedSeconds(string value, string key, string source) =>
        double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) &&
        double.IsFinite(seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : throw new InvalidDataException($"{source}: {key} must be a number of seconds greater than 0 (is '{value}').");
}
