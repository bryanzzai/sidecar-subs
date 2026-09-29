namespace SidecarSubs.Configuration;

public sealed record AppSettings(
    string OpenSubtitlesApiKey,
    string OpenSubtitlesUsername,
    string OpenSubtitlesPassword,
    string Language)
{
    public static AppSettings FromEnvironment()
    {
        var apiKey = Environment.GetEnvironmentVariable("SIDECARSUBS_OPENSUBTITLES_API_KEY") ?? string.Empty;
        var username = Environment.GetEnvironmentVariable("SIDECARSUBS_OPENSUBTITLES_USERNAME") ?? string.Empty;
        var password = Environment.GetEnvironmentVariable("SIDECARSUBS_OPENSUBTITLES_PASSWORD") ?? string.Empty;
        var language = Environment.GetEnvironmentVariable("SIDECARSUBS_LANGUAGE") ?? "en";

        return new AppSettings(apiKey, username, password, language);
    }

    public bool HasOpenSubtitlesCredentials =>
        !string.IsNullOrWhiteSpace(OpenSubtitlesApiKey)
        && !string.IsNullOrWhiteSpace(OpenSubtitlesUsername)
        && !string.IsNullOrWhiteSpace(OpenSubtitlesPassword);
}
