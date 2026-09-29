# Sidecar Subs

Windows desktop app for scanning a structured video library and downloading correctly named subtitle sidecar files.

## Status

Early bootstrap / v0.1.

The first implementation targets a local Windows video library and OpenSubtitles.com. It scans recursively, detects missing sidecar subtitles, searches OpenSubtitles, and writes an `.srt` beside the video using the exact same basename.

Example:

```text
Movie Name (2024) [1080p WEBRip].mkv
Movie Name (2024) [1080p WEBRip].srt
```

## Stack

- C#
- WPF
- .NET 10 LTS
- OpenSubtitles REST API
- No third-party NuGet dependencies in the initial build

## Build

Requirements:

- Windows 10/11
- .NET 10 SDK, or Visual Studio with the .NET desktop development workload

```powershell
dotnet build src/SidecarSubs/SidecarSubs.csproj
dotnet run --project src/SidecarSubs/SidecarSubs.csproj
```

## OpenSubtitles configuration

Credentials are deliberately **not** stored in the repository.

Set these environment variables before starting the app:

```powershell
$env:SIDECARSUBS_OPENSUBTITLES_API_KEY="your-api-key"
$env:SIDECARSUBS_OPENSUBTITLES_USERNAME="your-username"
$env:SIDECARSUBS_OPENSUBTITLES_PASSWORD="your-password"
```

The target language defaults to English. Override it with:

```powershell
$env:SIDECARSUBS_LANGUAGE="en"
```

## Initial workflow

1. Choose the root of the video library.
2. Scan recursively.
3. Existing `.srt` and `.en.srt` sidecars are detected.
4. Click **Download missing subtitles**.
5. Sidecar Subs searches by OpenSubtitles movie hash first.
6. If hash search fails, it falls back to a release-name/title query.
7. A successful subtitle is written as `<video-basename>.srt`.
8. Ambiguous or missing matches remain visible in the status list.

## Design goals

- Never modify or transcode video files.
- Preserve the existing hierarchical library.
- Conservative matching: exact hash matches are preferred.
- Provider abstraction so OpenSubtitles is not hard-wired into the rest of the app.
- Secrets stay out of Git.
- Cancellation and per-file progress for large libraries.

## Roadmap

- Secure credential storage using Windows Credential Manager.
- Review queue for ambiguous matches.
- Configurable `.srt` vs `.en.srt` naming.
- Dry-run mode and persistent logs.
- Automatic `Miss-Subtitles.txt` report.
- Packaging as a self-contained Windows executable.
