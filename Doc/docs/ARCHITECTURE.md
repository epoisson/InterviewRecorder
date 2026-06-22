# Architecture

Current design of the recording pipeline. This reflects the event-driven implementation and supersedes the older auto-save description in `INDEX.md`.

## Projects

Two projects under `src/`:

- **InterviewRecorder.Core** — Models + Services + the `IRecorder` interface. No WPF dependency, so any UI can reuse it.
- **InterviewRecorder.App** — the WPF UI. References Core and depends on `IRecorder`, never the concrete `RecordingOrchestrator`.

## Layers

```text
MainWindow (UI, InterviewRecorder.App)
  -> IRecorder  (interface; implemented by RecordingOrchestrator in Core)
       -> AudioCaptureEngine (owns the WAV file, chunks, rotation)
            -> IAudioCapture (MicrophoneCapture | LoopbackCapture)
       -> FFmpegService (background m4a conversion queue)
       -> FileManager / StateManager / RecoveryManager / LogManager / ConfigurationManager
```

## Capture to disk

NAudio delivers audio at real time through `DataAvailable` events. The engine writes those bytes straight to a `WaveFileWriter`, so pacing comes from the audio device, not from a polling loop.

- **MicrophoneCapture** and **LoopbackCapture** forward the device's `DataAvailable` event directly. No buffering layer, no pull loop.

The WAV is written in the capture's own format (option A): microphone as PCM16, loopback as 32-bit float. No resampling is done on the record path, which avoids format round-trips. FFmpeg normalises on compression.

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
3. Merges the chunk WAVs into `interview_<timestamp>.wav` (`FileManager.MergeChunksAsync`). Chunks that fail to open (e.g. a truncated chunk left by a crash) are skipped, so a single bad chunk never aborts the merge.
4. Merges the chunk m4a files into `interview_<timestamp>.m4a` using the FFmpeg concat demuxer with stream copy (`FFmpegService.MergeToM4a`).
5. Marks the session complete and writes the `.completed` marker.
6. Raises `RecordingMerged(fileName, status, size)` so the UI adds the merged file as a final grid row with its conversion status.

## Waveform

The engine raises `AudioPeak` with a normalised 0..1 peak, a few times per buffer, so the strip scrolls smoothly. `RecordingOrchestrator` re-raises this as `AudioLevel`. During playback the UI taps the player with a `MeteringSampleProvider` and uses its `StreamVolume` peaks. Both feed the same rolling buffer in `MainWindow`, rendered as a filled polygon.

## Threading model

| Work | Thread |
| --- | --- |
| UI, button handlers | WPF dispatcher thread |
| Microphone / loopback capture callbacks | NAudio capture threads |
| FFmpeg conversion worker | dedicated `Task` |
| Chunk writes | serialised by `_chunkLock` in the engine |
| Log file writes | serialised by a `SemaphoreSlim` in `LogManager` |

Cross-thread UI updates (waveform, log, state) marshal back via `Dispatcher.Invoke`.

## Configuration

`ConfigurationManager` reads `appsettings.json` with shared `JsonSerializerOptions`:

- `JsonStringEnumConverter`, so `CaptureMode` is a string such as `"InputDevice"`.
- `PropertyNameCaseInsensitive`.
- `[JsonPropertyName("AudioConfiguration")]` maps the file's root key to the model.

A `FileSystemWatcher` reloads on change. Changes during recording apply to the next recording, since the capture format is fixed when a recording starts.

## UI

`MainWindow` is a single window driven by orchestrator events; there is no MVVM layer.

- **Menu bar:** View (checkable items show/hide each panel), Device (lists input devices, ticks the active one, disabled unless idle), Config (session details, open `appsettings.json` in Notepad).
- **Status / Controls:** state, duration (active recording time, excluding paused periods), session id, Open Folder; Record/Stop and Pause/Resume are single toggle buttons; Play uses NAudio `WaveOutEvent`.
- **Content row:** Waveform, Log, and Chunks share one row of star columns separated by `GridSplitter`s; hiding a panel collapses its column.
- **Chunks grid:** bound to an `ObservableCollection<ChunkInfo>`. Rows are added on `ChunkStarted`, completed on `ChunkClosed` (end, duration, size), and their conversion column is driven by `ConversionStarted`/`ConversionCompleted` (matched to a row by chunk number). The merged file is appended on `RecordingMerged`.

## Recovery

On startup `RecoveryManager` scans session folders for a `metadata.json` without a `.completed` marker and offers the most recent for recovery. `RecordingOrchestrator.RecoverSessionAsync`:

1. Loads the session metadata (keeps the session id).
2. Rebuilds `ChunkFiles` from the chunks on disk — metadata is not always saved per chunk, so disk is the source of truth.
3. Sets the engine's next chunk index after the existing chunks (`InitializeForRecovery`), so resume continues at `chunk_<next>`, never overwriting from 0. Capture is created lazily on the first resume.
4. Recovers into the **Paused** state. The user can Resume (records into new chunks) or Stop (merges existing chunks).
5. Re-queues any chunk that has no matching m4a, so unconverted chunks finish converting as if just saved.

To keep recovery accurate, state is also saved after each chunk closes during normal recording, so a later crash leaves an up-to-date chunk list and duration.
