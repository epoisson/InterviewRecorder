# Architecture

Current design of the recording pipeline. This reflects the event-driven implementation and supersedes the older auto-save description in `INDEX.md`.

## Layers

```
MainWindow (UI)
  -> RecordingOrchestrator (coordinates a session)
       -> AudioCaptureEngine (owns the WAV file, chunks, rotation)
            -> IAudioCapture (MicrophoneCapture | LoopbackCapture | MixedCapture)
       -> FFmpegService (background m4a conversion queue)
       -> FileManager / StateManager / RecoveryManager / LogManager / ConfigurationManager
```

## Capture to disk

NAudio delivers audio at real time through `DataAvailable` events. The engine writes those bytes straight to a `WaveFileWriter`, so pacing comes from the audio device, not from a polling loop.

- **MicrophoneCapture** and **LoopbackCapture** forward the device's `DataAvailable` event directly. No buffering layer, no pull loop.
- **MixedCapture** mixes two live streams (microphone plus loopback). This is the one component that needs a pull loop, because a mixer must be read. It uses a `MixingSampleProvider` with `ReadFully = true` (so live inputs are never dropped when momentarily empty) and paces the loop to real time with a `Stopwatch`.

The WAV is written in the capture's own format (option A): microphone as PCM16, loopback and mix as 32-bit float. No resampling is done on the record path, which avoids format round-trips. FFmpeg normalises on compression.

## Chunking

`AudioCaptureEngine` writes a sequence of chunk files: `chunks/chunk_NNNN.wav`.

- On each incoming buffer the engine writes, then checks elapsed time against `ChunkDurationMinutes`.
- At the boundary it closes the current writer and opens the next **synchronously** inside the same lock, so there is no gap where audio is dropped.
- The just-closed chunk path is then queued to FFmpeg (fire-and-forget) for conversion.

Pause and stop both close the current chunk and queue it for conversion. Resume opens a fresh chunk. The chunk index is continuous across pause/resume.

## Compression queue

`FFmpegService` runs a single background worker over a `ConcurrentQueue`.

- `QueueConversion` enqueues a job (`chunk_NNNN.wav -> chunk_NNNN.m4a`).
- The worker converts one job at a time with `ffmpeg -codec:a <codec> -b:a <bitrate>k`.
- `Stop` drains the queue: it sets a drain flag and waits for every pending job to finish, with a 2-minute safety timeout that falls back to cancelling. This is deliberate, so the final chunk (queued at stop) is always converted rather than cancelled.

## Stop and merge

`RecordingOrchestrator.StopRecordingAsync`:

1. Stops capture (final chunk closed and queued).
2. Drains the FFmpeg queue.
3. Merges the chunk WAVs into `interview_<timestamp>.wav` (`FileManager.MergeChunksAsync`).
4. Merges the chunk m4a files into `interview_<timestamp>.m4a` using the FFmpeg concat demuxer with stream copy (`FFmpegService.MergeToM4a`).
5. Marks the session complete and writes the `.completed` marker.

## Waveform

The engine raises `AudioPeak` with a normalised 0..1 peak, a few times per buffer, so the strip scrolls smoothly. `RecordingOrchestrator` re-raises this as `AudioLevel`. During playback the UI taps the player with a `MeteringSampleProvider` and uses its `StreamVolume` peaks. Both feed the same rolling buffer in `MainWindow`, rendered as a filled polygon.

## Threading model

| Work | Thread |
| --- | --- |
| UI, button handlers | WPF dispatcher thread |
| Microphone / loopback capture callbacks | NAudio capture threads |
| Mix pull loop | dedicated `Task` |
| FFmpeg conversion worker | dedicated `Task` |
| Chunk writes | serialised by `_chunkLock` in the engine |
| Log file writes | serialised by a `SemaphoreSlim` in `LogManager` |

Cross-thread UI updates (waveform, log, state) marshal back via `Dispatcher.Invoke`.

## Configuration

`ConfigurationManager` reads `appsettings.json` with shared `JsonSerializerOptions`:

- `JsonStringEnumConverter`, so `CaptureMode` is a string such as `"Mix"`.
- `PropertyNameCaseInsensitive`.
- `[JsonPropertyName("AudioConfiguration")]` maps the file's root key to the model.

A `FileSystemWatcher` reloads on change. Changes during recording apply to the next recording, since the capture format is fixed when a recording starts.

## Recovery

On startup `RecoveryManager` scans session folders for a `metadata.json` without a `.completed` marker and offers the most recent for recovery.
