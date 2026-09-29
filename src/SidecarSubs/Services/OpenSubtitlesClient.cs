using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using SidecarSubs.Configuration;

namespace SidecarSubs.Services;

public sealed class OpenSubtitlesClient : ISubtitleProvider
{
    private const string UserAgent = "SidecarSubs v0.1.0";

    private readonly AppSettings _settings;
    private readonly HttpClient _apiClient;
    private readonly HttpClient _downloadClient;
    private readonly SemaphoreSlim _requestGate = new(1, 1);

    private Uri _apiBaseUri = new("https://api.opensubtitles.com/api/v1/");
    private string? _token;

    public OpenSubtitlesClient(AppSettings settings)
    {
        _settings = settings;
        _apiClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _downloadClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(2)
        };
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (!_settings.HasOpenSubtitlesCredentials)
            throw new InvalidOperationException(
                "OpenSubtitles credentials are missing. Configure the SIDECARSUBS_OPENSUBTITLES_* environment variables.");

        using var request = CreateRequest(HttpMethod.Post, new Uri(_apiBaseUri, "login"));
        request.Content = JsonContent.Create(new
        {
            username = _settings.OpenSubtitlesUsername,
            password = _settings.OpenSubtitlesPassword
        });

        using var response = await SendApiAsync(request, cancellationToken);
        var json = await ReadJsonAsync(response, cancellationToken);

        _token = GetString(json.RootElement, "token")
            ?? throw new InvalidOperationException("OpenSubtitles login did not return a token.");

        var baseUrl = GetString(json.RootElement, "base_url");
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            if (!baseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                baseUrl = "https://" + baseUrl;

            _apiBaseUri = new Uri(baseUrl.TrimEnd('/') + "/api/v1/");
        }
    }

    public async Task<SubtitleMatch?> FindBestMatchAsync(
        string videoPath,
        string language,
        CancellationToken cancellationToken)
    {
        var movieHash = await OpenSubtitlesHash.ComputeAsync(videoPath, cancellationToken);

        var hashUri = new Uri(
            _apiBaseUri,
            $"subtitles?moviehash={Uri.EscapeDataString(movieHash)}&languages={Uri.EscapeDataString(language)}");

        var hashMatches = await SearchAsync(hashUri, searchedByHash: true, cancellationToken);
        var bestHash = hashMatches.OrderByDescending(x => x.Score).FirstOrDefault();

        if (bestHash is not null)
            return bestHash;

        var query = ReleaseNameParser.BuildSearchQuery(videoPath);
        if (string.IsNullOrWhiteSpace(query))
            return null;

        var queryUri = new Uri(
            _apiBaseUri,
            $"subtitles?query={Uri.EscapeDataString(query)}&languages={Uri.EscapeDataString(language)}");

        var nameMatches = await SearchAsync(queryUri, searchedByHash: false, cancellationToken);
        return nameMatches.OrderByDescending(x => x.Score).FirstOrDefault();
    }

    public async Task DownloadAsync(
        SubtitleMatch match,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, new Uri(_apiBaseUri, "download"));
        request.Content = JsonContent.Create(new
        {
            file_id = match.FileId,
            sub_format = "srt"
        });

        using var response = await SendApiAsync(request, cancellationToken);
        var json = await ReadJsonAsync(response, cancellationToken);

        var link = GetString(json.RootElement, "link")
            ?? throw new InvalidOperationException("OpenSubtitles download response did not contain a link.");

        // Deliberately use a separate HttpClient here so API credentials and JWT
        // are never forwarded to the signed subtitle download URL.
        await using var source = await _downloadClient.GetStreamAsync(link, cancellationToken);

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var tempPath = destinationPath + ".part";

        try
        {
            await using (var destination = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }

            File.Move(tempPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private async Task<IReadOnlyList<SubtitleMatch>> SearchAsync(
        Uri uri,
        bool searchedByHash,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, uri);
        using var response = await SendApiAsync(request, cancellationToken);
        var json = await ReadJsonAsync(response, cancellationToken);

        if (!json.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
            return [];

        var matches = new List<SubtitleMatch>();

        foreach (var entry in data.EnumerateArray())
        {
            if (!entry.TryGetProperty("attributes", out var attributes))
                continue;

            if (!attributes.TryGetProperty("files", out var files)
                || files.ValueKind != JsonValueKind.Array)
                continue;

            var trusted = GetBool(attributes, "from_trusted");
            var hearingImpaired = GetBool(attributes, "hearing_impaired");
            var machineTranslated = GetBool(attributes, "machine_translated");
            var aiTranslated = GetBool(attributes, "ai_translated");
            var downloadCount = GetDouble(attributes, "download_count");
            var rating = GetDouble(attributes, "ratings");

            foreach (var file in files.EnumerateArray())
            {
                var fileId = GetInt64(file, "file_id");
                if (fileId is null)
                    continue;

                var score = searchedByHash ? 1000d : 0d;
                if (trusted) score += 100d;
                if (!machineTranslated) score += 20d;
                if (!aiTranslated) score += 20d;
                if (!hearingImpaired) score += 10d;
                score += Math.Min(downloadCount / 100d, 50d);
                score += Math.Min(rating, 10d);

                matches.Add(new SubtitleMatch(
                    FileId: fileId.Value,
                    Provider: "OpenSubtitles",
                    SourceFileName: GetString(file, "file_name"),
                    MovieHashMatch: searchedByHash,
                    Score: score));
            }
        }

        return matches;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation("Api-Key", _settings.OpenSubtitlesApiKey);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

        if (!string.IsNullOrWhiteSpace(_token))
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _token);

        return request;
    }

    private async Task<HttpResponseMessage> SendApiAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        await _requestGate.WaitAsync(cancellationToken);

        try
        {
            var response = await _apiClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
                response.Dispose();

                await Task.Delay(delay, cancellationToken);

                throw new HttpRequestException(
                    "OpenSubtitles rate limit reached. Retry the operation.",
                    null,
                    HttpStatusCode.TooManyRequests);
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                response.Dispose();

                throw new HttpRequestException(
                    $"OpenSubtitles returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
            }

            return response;
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool GetBool(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
        && value.GetBoolean();

    private static double GetDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return 0;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            return number;

        return 0;
    }

    private static long? GetInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            return number;

        return null;
    }

    public ValueTask DisposeAsync()
    {
        _requestGate.Dispose();
        _apiClient.Dispose();
        _downloadClient.Dispose();
        return ValueTask.CompletedTask;
    }
}
