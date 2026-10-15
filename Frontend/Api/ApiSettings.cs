using System.IO;
using System.Text.Json;

namespace Frontend.Api;

/// <summary>
/// Backend settings, read from Api/api-settings.json next to the executable (edit that file to point the app at
/// another backend without rebuilding). Missing or invalid values fall back to the defaults below.
/// </summary>
public sealed record ApiSettings(string BaseUrl, TimeSpan RequestTimeout, TimeSpan StatusInterval)
{
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

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "Api", "api-settings.json");

    private static TimeSpan Seconds(JsonElement root, string name, TimeSpan fallback) =>
        root.TryGetProperty(name, out var value) && value.TryGetDouble(out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : fallback;
}
