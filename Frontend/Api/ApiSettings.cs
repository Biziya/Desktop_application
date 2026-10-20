using System.IO;
using System.Text.Json;

namespace Frontend.Api;

/// <summary>
/// Backend settings. Lookup order, later wins:
/// 1. Api/api-settings.json next to the executable: the shipped defaults. Missing or invalid values fall back to
///    the defaults below (unchanged behaviour).
/// 2. %LocalAppData%\CT007_Measurement_Studio\.env: per-user settings, outside the Velopack install folder so an
///    update keeps them (see Frontend/.env.example).
/// 3. API_BASE_URL, API_TIMEOUT_SECONDS and API_RETRY_INTERVAL_SECONDS environment variables.
/// Values from 2 and 3 are checked strictly (<see cref="WithOverrides"/>): a wrong value stops the app with a message
/// instead of silently pointing it at another backend.
/// </summary>
public sealed record ApiSettings(string BaseUrl, TimeSpan RequestTimeout, TimeSpan StatusInterval)
{
    public const string BaseUrlKey = "API_BASE_URL";
    public const string TimeoutKey = "API_TIMEOUT_SECONDS";
    public const string RetryIntervalKey = "API_RETRY_INTERVAL_SECONDS";

    public static ApiSettings Default { get; } = new("http://localhost:5002", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

    public static ApiSettings Load(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;

            return new ApiSettings(
                root.TryGetProperty("baseUrl", out var url) && Uri.TryCreate(url.GetString(), UriKind.Absolute, out _)
                    ? url.GetString()! : Default.BaseUrl,
                Seconds(root, "requestTimeoutSeconds", Default.RequestTimeout),
                Seconds(root, "statusIntervalSeconds", Default.StatusInterval));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return Default;
        }
    }

    /// <summary>The settings the app uses: api-settings.json, then the user's .env file, then environment variables.</summary>
    /// <exception cref="InvalidDataException">The .env file or an API_* environment variable has an invalid value.</exception>
    public static ApiSettings LoadConfigured() => Load(DefaultPath).WithOverrides(EnvFilePath, Environment.GetEnvironmentVariable);

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "Api", "api-settings.json");

    /// <summary>%LocalAppData%\CT007_Measurement_Studio\.env, next to the measurement database.</summary>
    public static string EnvFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CT007_Measurement_Studio", ".env");

    /// <summary>
    /// Applies the API_* values from <paramref name="envFile"/> (when it exists) and from <paramref name="environment"/>
    /// (which wins). Keys not set in either keep this instance's value.
    /// </summary>
    /// <exception cref="InvalidDataException">A line or value is invalid; the message names the source and key.</exception>
    public ApiSettings WithOverrides(string envFile, Func<string, string?> environment)
    {
        var file = File.Exists(envFile) ? ReadEnvFile(envFile) : [];
        var settings = this;

        foreach (var key in new[] { BaseUrlKey, TimeoutKey, RetryIntervalKey })
        {
            var (value, source) = environment(key) is { Length: > 0 } variable ? (variable, "Environment variable")
                : file.TryGetValue(key, out var line) ? (line, envFile)
                : (null, "");

            settings = value is null ? settings : key switch
            {
                BaseUrlKey => settings with { BaseUrl = CheckedUrl(value, source) },
                TimeoutKey => settings with { RequestTimeout = CheckedSeconds(value, key, source) },
                _ => settings with { StatusInterval = CheckedSeconds(value, key, source) }
            };
        }

        return settings;
    }

    private static TimeSpan Seconds(JsonElement root, string name, TimeSpan fallback) =>
        root.TryGetProperty(name, out var value) && value.TryGetDouble(out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : fallback;

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
