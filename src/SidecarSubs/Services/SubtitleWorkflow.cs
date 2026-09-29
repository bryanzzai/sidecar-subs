namespace SidecarSubs.Services;

public sealed class SubtitleWorkflow(ISubtitleProvider provider)
{
    public async Task<SubtitleResult> DownloadForVideoAsync(
        string videoPath,
        string language,
        CancellationToken cancellationToken)
    {
        var match = await provider.FindBestMatchAsync(videoPath, language, cancellationToken);

        if (match is null)
            return new SubtitleResult(false, null, "No subtitle match found.");

        var destination = Path.ChangeExtension(videoPath, ".srt");

        await provider.DownloadAsync(match, destination, cancellationToken);

        return new SubtitleResult(
            true,
            destination,
            match.MovieHashMatch
                ? "Downloaded (movie hash match)."
                : "Downloaded (name match).");
    }
}

public sealed record SubtitleResult(
    bool Success,
    string? SubtitlePath,
    string Message);
