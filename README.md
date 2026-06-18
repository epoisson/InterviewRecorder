# Interview Recorder

![Build](https://github.com/etiennepoisson/InterviewRecorder/actions/workflows/build.yml/badge.svg)
![License: Unlicense](https://img.shields.io/badge/license-Unlicense-blue.svg)
![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4.svg)

Windows desktop app for recording interviews to disk as chunked WAV, compressing each chunk to m4a in the background, and merging both into single files when recording stops.

- **Platform:** WPF on .NET 9 (`net9.0-windows`)
- **Audio:** [NAudio](https://github.com/naudio/NAudio)
- **Compression:** external [FFmpeg](https://ffmpeg.org/) process

## Features

- Two capture modes: microphone or system audio (loopback).
- Chunked recording: audio is written in fixed-length WAV chunks, so a crash loses at most one chunk.
- Background compression: each finished chunk is queued to FFmpeg and converted to m4a while recording continues.
- Merge on stop: chunk WAVs merge into one WAV; chunk m4a files merge into one m4a, saved beside it.
- Pause and resume: each pause closes and queues the current chunk; resume opens a new one.
- Crash recovery: incomplete sessions are detected on startup and reopened in a paused state; resume continues at the next chunk, and any chunk missing its m4a is re-queued for conversion.
- Live scrolling waveform during recording and playback.
- In-app playback of the last recording.
- Chunks grid showing each chunk's number, start/end, duration, size, and conversion status (Processing / Done / Failed), plus a row for the merged file.
- Menu bar: View (show/hide panels), Device (pick the input device, idle only), Config (session details, open config in Notepad).
- Resizable panels: Waveform, Log, and Chunks sit in one row with drag splitters, each toggleable from the View menu.
- Open Folder button to reveal the session directory in Explorer.

## Requirements

- .NET 9 SDK.
- FFmpeg on `PATH` (or set an explicit path in config). Compression is skipped with a warning if FFmpeg is not found.

## Build and run

```bash
dotnet build
dotnet run
```

## Output layout

Recordings are written under `Documents\InterviewRecordings\<sessionId>\`:

```
<sessionId>/
  chunks/
    chunk_0000.wav   chunk_0000.m4a
    chunk_0001.wav   chunk_0001.m4a
    ...
  interview_<timestamp>.wav    # all chunk WAVs merged
  interview_<timestamp>.m4a    # all chunk m4a files merged
  metadata.json                # session state
  session.log                  # event log
  .completed                   # marker written when finalised
```

## Configuration

Settings live in `Documents\InterviewRecordings\appsettings.json`. A default file is created on first run. The file is watched, so edits apply on the next recording.

```json
{
  "AudioConfiguration": {
    "CaptureMode": "InputDevice",
    "SampleRate": 44100,
    "BitsPerSample": 16,
    "Channels": 1,
    "ChunkDurationMinutes": 1,
    "InputDevice": { "Enabled": true, "DeviceId": 0 },
    "SystemAudioDevice": { "DeviceId": 0 },
    "Compression": {
      "Enabled": true,
      "Codec": "aac",
      "Bitrate": 128,
      "Format": "m4a",
      "FFmpegPath": "ffmpeg",
      "DeleteWavAfterConversion": false
    }
  }
}
```

### Key settings

| Setting | Values | Effect |
| --- | --- | --- |
| `CaptureMode` | `InputDevice`, `Loopback` | What audio source is recorded. |
| `SampleRate` | 8000–192000 | WAV sample rate for microphone capture. Loopback uses the device's native rate. |
| `Channels` | 1 or 2 | Channel count for microphone capture. |
| `ChunkDurationMinutes` | 1–60 | Length of each chunk before rotation. |
| `Compression.Codec` | `aac`, `libopus` | FFmpeg audio codec. Invalid values reset to `aac`. |
| `Compression.Bitrate` | integer kbps | Target bitrate, for example `128`. |
| `Compression.FFmpegPath` | path or `ffmpeg` | FFmpeg executable. Defaults to `ffmpeg` on `PATH`. |
| `Compression.DeleteWavAfterConversion` | bool | Delete each chunk WAV once its m4a is produced. Leave `false` to keep the merged WAV. |

`CaptureMode` is written and read as a string. Enum values are case-insensitive.

## Notes

- Playback uses the merged WAV.

## Architecture

See [Doc/docs/ARCHITECTURE.md](Doc/docs/ARCHITECTURE.md) for the capture, chunking, compression, and threading model.

## License

This project's own code is released into the public domain under the [Unlicense](LICENSE).

Third-party components keep their own licenses (NAudio is MIT; FFmpeg is invoked as an external program). See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
