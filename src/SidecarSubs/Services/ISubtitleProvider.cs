namespace SidecarSubs.Services;

public interface ISubtitleProvider : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<SubtitleMatch?> FindBestMatchAsync(
        string videoPath,
        string language,
        CancellationToken cancellationToken);

    Task DownloadAsync(
        SubtitleMatch match,
        string destinationPath,
        CancellationToken cancellationToken);
}

public sealed record SubtitleMatch(
    long FileId,
    string Provider,
    string? SourceFileName,
    bool MovieHashMatch,
    double Score);
