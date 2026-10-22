using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace Frontend.Api;

/// <summary>
/// The one HTTP setup for the backend: base URL, timeout and JSON options. Every request reports whether the backend
/// answered at all (<see cref="Reached"/>), which drives the sidebar Online/Offline indicator without extra requests.
/// Continuations run on the caller's context, so results and events arrive on the UI thread.
/// </summary>
public sealed class ApiClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient http;

    public ApiClient(ApiSettings settings, HttpMessageHandler? handler = null)
    {
        http = handler is null ? new HttpClient() : new HttpClient(handler);
        http.BaseAddress = new Uri(settings.BaseUrl);
        http.Timeout = settings.RequestTimeout;
        BaseUrl = settings.BaseUrl;
    }

    public string BaseUrl { get; }

    /// <summary>Raised after every request: true when any HTTP response arrived (even an error status).</summary>
    public event Action<bool>? Reached;

    public Task<ApiResult<T>> GetAsync<T>(string path, CancellationToken cancellationToken) =>
        SendAsync<T>(token => http.GetAsync(path, token), showProblemDetail: false, cancellationToken);

    /// <summary>
    /// Posts <paramref name="body"/> as JSON. An error answer's ProblemDetails text (the backend's reason, written for
    /// users: a validation or duplicate message, never internal details) becomes <see cref="ApiResult{T}.Error"/>.
    /// </summary>
    public Task<ApiResult<T>> PostAsync<T>(string path, object body, CancellationToken cancellationToken) =>
        SendAsync<T>(token => http.PostAsJsonAsync(path, body, Json, token), showProblemDetail: true, cancellationToken);

    private async Task<ApiResult<T>> SendAsync<T>(
        Func<CancellationToken, Task<HttpResponseMessage>> send, bool showProblemDetail, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            response = await send(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            // Refused connection, network failure, or HttpClient.Timeout elapsing (TaskCanceledException).
            Reached?.Invoke(false);
            return ApiResult<T>.Failed(ApiStatus.Unreachable, error is TaskCanceledException ? "Backend did not respond in time." : "Backend is offline.");
        }

        Reached?.Invoke(true);

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var detail = showProblemDetail ? await ProblemDetailAsync(response, cancellationToken) : null;
                return ApiResult<T>.Failed(ApiStatus.HttpError, detail ?? $"Backend error {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken) is { } value
                    ? ApiResult<T>.Ok(value)
                    : ApiResult<T>.Failed(ApiStatus.InvalidResponse, "Backend returned no data.");
            }
            catch (Exception error) when (error is JsonException or HttpRequestException or System.IO.IOException)
            {
                return ApiResult<T>.Failed(ApiStatus.InvalidResponse, "Backend returned unexpected data.");
            }
        }
    }

    // "detail" of an application/problem+json answer, else the first model-validation error; null when there is none.
    private static async Task<string?> ProblemDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentType?.MediaType != "application/problem+json")
        {
            return null;
        }

        try
        {
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = problem.RootElement;

            if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
            {
                return detail.GetString();
            }

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                return errors.EnumerateObject().SelectMany(e => e.Value.EnumerateArray()).Select(m => m.GetString()).FirstOrDefault();
            }

            return null;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or HttpRequestException or System.IO.IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reachability check without downloading anything: a HEAD request to the base URL. Any HTTP answer, even 404 or
    /// 405, means the backend is reachable. Reports through <see cref="Reached"/> like every other request.
    /// </summary>
    public async Task<bool> ProbeAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, "");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            Reached?.Invoke(false);
            return false;
        }

        Reached?.Invoke(true);
        return true;
    }

    public void Dispose() => http.Dispose();
}
