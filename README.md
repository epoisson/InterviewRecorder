# Interview Recorder

Windows desktop app for recording interviews to disk as chunked WAV, compressing each chunk to m4a in the background, and merging both into single files when recording stops.

- **Platform:** WPF on .NET 9 (`net9.0-windows`)
- **Audio:** [NAudio](https://github.com/naudio/NAudio)
- **Compression:** external [FFmpeg](https://ffmpeg.org/) process

## Features

- Three capture modes: microphone, system audio (loopback), or a mix of both.
- Chunked recording: audio is written in fixed-length WAV chunks, so a crash loses at most one chunk.
- Background compression: each finished chunk is queued to FFmpeg and converted to m4a while recording continues.
- Merge on stop: chunk WAVs merge into one WAV; chunk m4a files merge into one m4a, saved beside it.
- Pause and resume: each pause closes and queues the current chunk; resume opens a new one.
- Crash recovery: incomplete sessions are detected on startup and offered for recovery.
- Live scrolling waveform during recording and playback.
- In-app playback of the last recording.

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
    "CaptureMode": "Mix",
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
| `CaptureMode` | `InputDevice`, `Loopback`, `Mix` | What audio source is recorded. |
| `SampleRate` | 8000–192000 | WAV sample rate for microphone capture. Loopback uses the device's native rate. |
| `Channels` | 1 or 2 | Channel count for microphone capture. |
| `ChunkDurationMinutes` | 1–60 | Length of each chunk before rotation. |
| `Compression.Codec` | `aac`, `libopus` | FFmpeg audio codec. Invalid values reset to `aac`. |
| `Compression.Bitrate` | integer kbps | Target bitrate, for example `128`. |
| `Compression.FFmpegPath` | path or `ffmpeg` | FFmpeg executable. Defaults to `ffmpeg` on `PATH`. |
| `Compression.DeleteWavAfterConversion` | bool | Delete each chunk WAV once its m4a is produced. Leave `false` to keep the merged WAV. |

`CaptureMode` is written and read as a string. Enum values are case-insensitive.

## Notes

- In Mix mode the microphone opens at the loopback device's sample rate (commonly 48000 Hz). A microphone that rejects that rate will fail to start Mix.
- Playback uses the merged WAV.

## Architecture

See [Doc/docs/ARCHITECTURE.md](Doc/docs/ARCHITECTURE.md) for the capture, chunking, compression, and threading model.
