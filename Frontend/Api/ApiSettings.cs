using System.IO;
using System.Reflection;

namespace Frontend.Api;

/// <summary>
/// Backend settings, read once at start-up from the .env file of the selected environment (docs/API_CONFIGURATION.md).
/// 1. APP_ENV selects the environment (local or production): the APP_ENV environment variable, otherwise the value
///    compiled into the build (AppEnvironment in Frontend.csproj: Debug builds local, other builds production).
///    It is never read from a .env file, because the file is chosen by it.
/// 2. The first existing .env.&lt;APP_ENV&gt; file of <see cref="EnvFileCandidates"/>: the per-user
///    %LocalAppData%\CT007_Measurement_Studio\Config folder (outside the Velopack install folder, so updates keep it),
///    then the Frontend project folder when running from source.
/// 3. API_BASE_URL, API_TIMEOUT_SECONDS and API_RETRY_INTERVAL_SECONDS environment variables override the file, key by key.
/// API_BASE_URL is required; the two timings default to 5 s. Every problem stops the app with a message instead of
/// silently pointing it at another backend: there is no fallback from one environment to the other.
/// </summary>
public sealed record ApiSettings(string BaseUrl, TimeSpan RequestTimeout, TimeSpan StatusInterval)
{
    public const string EnvironmentKey = "APP_ENV";
    public const string BaseUrlKey = "API_BASE_URL";
    public const string TimeoutKey = "API_TIMEOUT_SECONDS";
    public const string RetryIntervalKey = "API_RETRY_INTERVAL_SECONDS";

    public static IReadOnlyList<string> Environments { get; } = ["local", "production"];

    // Optional values only; the backend address never has a default.
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultStatusInterval = TimeSpan.FromSeconds(5);

    /// <summary>The settings the app uses (environment variables, the selected .env file, the build's environment).</summary>
    /// <exception cref="InvalidDataException">The configuration is missing or invalid; the message says what to fix.</exception>
    public static ApiSettings LoadConfigured()
    {
        var environment = SelectEnvironment(Environment.GetEnvironmentVariable, BuildEnvironment);
        return Load(environment, EnvFileCandidates(environment, ConfigDirectory, ProjectDirectory(AppContext.BaseDirectory)),
            Environment.GetEnvironmentVariable);
    }

    /// <summary>%LocalAppData%\CT007_Measurement_Studio\Config: per user, beside measurements.db, kept by updates.</summary>
    public static string ConfigDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CT007_Measurement_Studio", "Config");

    /// <summary>AppEnvironment from Frontend.csproj, compiled into the assembly.</summary>
    public static string? BuildEnvironment => typeof(ApiSettings).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == "AppEnvironment")?.Value;

    /// <summary>The APP_ENV variable when set, otherwise <paramref name="buildEnvironment"/>; must be local or production.</summary>
    /// <exception cref="InvalidDataException">Neither is set, or the value is not a supported environment.</exception>
    public static string SelectEnvironment(Func<string, string?> environment, string? buildEnvironment)
    {
        var (value, source) = environment(EnvironmentKey) is { Length: > 0 } variable
            ? (variable.Trim(), $"Environment variable {EnvironmentKey}")
            : (buildEnvironment ?? "", "The build's AppEnvironment");

        return Environments.Contains(value)
            ? value
            : throw new InvalidDataException($"{source} is '{value}'. Use one of: {string.Join(", ", Environments)}.");
    }

    /// <summary>
    /// Where .env.&lt;environment&gt; is looked for, in order: the per-user config folder, then the Frontend project
    /// folder (only when the app runs from a source checkout, i.e. <paramref name="projectDirectory"/> is not null).
    /// </summary>
    public static IReadOnlyList<string> EnvFileCandidates(string environment, string configDirectory, string? projectDirectory) =>
        projectDirectory is null
            ? [Path.Combine(configDirectory, $".env.{environment}")]
            : [Path.Combine(configDirectory, $".env.{environment}"), Path.Combine(projectDirectory, $".env.{environment}")];

    /// <summary>The folder holding Frontend.csproj above <paramref name="baseDirectory"/> (bin\Debug\...), or null when installed.</summary>
    public static string? ProjectDirectory(string baseDirectory)
    {
        for (var directory = new DirectoryInfo(baseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Frontend.csproj")))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads the first existing file of <paramref name="candidates"/> (only that one), then applies the API_*
    /// values of <paramref name="variables"/>, which win key by key.
    /// </summary>
    /// <exception cref="InvalidDataException">A file, line or value is invalid, or API_BASE_URL is not set anywhere.</exception>
    public static ApiSettings Load(string environment, IReadOnlyList<string> candidates, Func<string, string?> variables)
    {
        var envFile = candidates.FirstOrDefault(File.Exists);
        var file = envFile is null ? [] : ReadEnvFile(envFile);

        // The file was chosen by APP_ENV; an APP_ENV line inside it may only confirm that choice.
        if (file.TryGetValue(EnvironmentKey, out var declared) && declared != environment)
        {
            throw new InvalidDataException(
                $"{envFile}: {EnvironmentKey}={declared}, but this file was selected for {EnvironmentKey}={environment}. Fix or remove the line.");
        }

        string? baseUrl = null;
        var timeout = DefaultTimeout;
        var interval = DefaultStatusInterval;

        foreach (var key in new[] { BaseUrlKey, TimeoutKey, RetryIntervalKey })
        {
            var (value, source) = variables(key) is { Length: > 0 } variable ? (variable, "Environment variable")
                : file.TryGetValue(key, out var line) && line.Length > 0 ? (line, envFile!)
                : (null, "");

            switch (value is null ? null : key)
            {
                case BaseUrlKey: baseUrl = CheckedUrl(value!, source); break;
                case TimeoutKey: timeout = CheckedSeconds(value!, key, source); break;
                case RetryIntervalKey: interval = CheckedSeconds(value!, key, source); break;
            }
        }

        return baseUrl is null
            ? throw new InvalidDataException(MissingBaseUrl(environment, candidates, envFile))
            : new ApiSettings(baseUrl, timeout, interval);
    }

    private static string MissingBaseUrl(string environment, IReadOnlyList<string> candidates, string? envFile)
    {
        var where = envFile is null
            ? $"No configuration file was found for {EnvironmentKey}={environment}. Looked for:\n" +
              string.Join("\n", candidates.Select(path => "  " + path)) +
              $"\n\nCreate {candidates[0]} from .env.example and set {BaseUrlKey} to the backend address, or set the {BaseUrlKey} environment variable."
            : $"{envFile} does not set {BaseUrlKey}. Add the backend address, for example {BaseUrlKey}=https://ct007-api.example.com.";

        // Earlier versions read a single .env file one folder up; say so instead of ignoring it silently.
        var legacy = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(candidates[0])!)!, ".env");

        return File.Exists(legacy)
            ? $"{where}\n\n{legacy} is no longer read: move its values to {candidates[0]}."
            : where;
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
