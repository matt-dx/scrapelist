# scrapelist

`scrapelist` is a terminal UI app that downloads a YouTube video or playlist, transcodes the result with FFmpeg, and writes a ready-to-play `.m3u` playlist.

It supports:

- audio-only downloads (`.m4a`)
- video-only downloads (`.m4v`)
- both audio and video in one run

The app shows live status panels for pending, downloading, transcoding, completed/skipped, and failed items.

## Requirements

- .NET SDK that can build `net10.0`
- Internet access (YouTube + FFmpeg download source)

No manual FFmpeg install is required in most cases:

- If `ffmpeg` is on `PATH`, it is used.
- Otherwise the app downloads FFmpeg into a local `.tools` folder at runtime.

## Quick start

From the project root:

```bash
dotnet run -- "https://www.youtube.com/playlist?list=YOUR_PLAYLIST_ID"
```

Single video:

```bash
dotnet run -- "https://www.youtube.com/watch?v=VIDEO_ID"
```

Audio-only to a specific folder:

```bash
dotnet run -- "https://www.youtube.com/playlist?list=YOUR_PLAYLIST_ID" --type audio --output "D:\Music"
```

## CLI usage

```text
scrapelist <uri> [options]
```

### Arguments

- `uri` (required): YouTube video or playlist URL

### Options

- `--type <audio|video|both>`

  - What to produce
  - Default: `both`

- `--retries <int>`

  - Max retries per item (download/transcode failures)
  - Default: `3`

- `--parallel <int>`

  - Max parallel download workers
  - Default: `3`

- `--indexed`

  - Prefix filenames with playlist index (`[N] - ...`)
  - Default: `false`

- `--timeout <seconds>`

  - Per-item inactivity timeout while downloading
  - Default: `60`

- `--output <path>`

  - Output directory
  - Default: `.`

- `--codec <x264|x265>`

  - Video encoder used during mux/transcode
  - Default: `x265`

- `--debug`

  - Write a debug log file in the output folder (`.debug-YYYYMMDD-HHMMSS.log`)
  - Default: `false`

## What gets created

Depending on `--type`, each item outputs:

- `audio`: `Channel - Title.m4a`
- `video`: `Channel - Title.m4v`
- `both`: both files above

If the input is a playlist, the app also creates:

- `~Playlist Title.m3u`

The `.m3u` contains `#EXTINF` metadata and references completed/skipped files in playlist order.

## Runtime behavior

- Existing final files are detected up front and marked `Skipped`.
- Downloads support resume via `.part` files.
- Downloaded media is transcoded/muxed by FFmpeg before final rename.
- Transcoding runs in a background worker (up to 2 parallel transcodes).
- Playlist file is updated during progress and written again at completion.

## File naming and sanitization

- Output names are built as `ChannelName - Title`.
- Invalid filename characters are replaced with Unicode-safe alternatives.
- Very long names are truncated to a safe length.
- With `--indexed`, names become `[PlaylistIndex] - ChannelName - Title`.

## Examples

Video only with H.264:

```bash
dotnet run -- "https://www.youtube.com/playlist?list=YOUR_PLAYLIST_ID" --type video --codec x264
```

Higher download parallelism and indexed filenames:

```bash
dotnet run -- "https://www.youtube.com/playlist?list=YOUR_PLAYLIST_ID" --parallel 5 --indexed
```

Debug run:

```bash
dotnet run -- "https://www.youtube.com/watch?v=VIDEO_ID" --debug --output "D:\Downloads\scrapelist"
```

## Troubleshooting

- **Invalid URL**

  - Ensure `uri` is a full absolute YouTube URL.

- **Timeouts**

  - Increase `--timeout` for slower connections.

- **FFmpeg issues**

  - Re-run with `--debug` and inspect the generated debug log in the output directory.
  - If auto-download fails, install FFmpeg manually and ensure it is on `PATH`.

- **Some items fail repeatedly**

  - Retry with a higher `--retries` value.

## Development

Build:

```bash
dotnet build
```

Run:

```bash
dotnet run -- "https://www.youtube.com/watch?v=VIDEO_ID"
```
