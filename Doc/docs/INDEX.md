# Interview Recorder - Documentation

## Where to start

- [README](https://github.com/epoisson/InterviewRecorder/blob/main/README.md) - overview, features, build/run, configuration reference.
- [ARCHITECTURE.md](ARCHITECTURE.md) - the current design: capture, chunking, compression queue, stop/merge, UI, threading, and recovery. This is the authoritative description.
- [QUICK_REFERENCE.md](QUICK_REFERENCE.md) - developer quick reference (flows, config, components).
- [workspace.dsl](https://github.com/epoisson/InterviewRecorder/blob/main/Doc/workspace.dsl) - C4 model (Structurizr DSL): context, container, component, and dynamic views.

## Viewing the C4 diagrams

The model lives in `workspace.dsl`. View it with Structurizr Lite:

```bash
docker run -it --rm -p 8080:8080 -v "%cd%/Doc":/usr/local/structurizr structurizr/lite
# then open http://localhost:8080
```

Or upload `workspace.dsl` to <https://structurizr.com>, or use the Structurizr DSL extension in VS Code.

For a no-tooling view, open the prebuilt offline viewer at [`../static/index.html`](../static/index.html). Mermaid exports also live in [`../diagrams/`](../diagrams/). Both are generated from `workspace.dsl`:

```bash
# from the repo root
wsl bash -c 'docker run --rm -v "$(pwd)/Doc:/usr/local/structurizr" structurizr/structurizr export -workspace workspace.dsl -format static -output static'
wsl bash -c 'docker run --rm -v "$(pwd)/Doc:/usr/local/structurizr" structurizr/structurizr export -workspace workspace.dsl -format mermaid -output diagrams'
```

## Component summary

| Component | Type | Purpose |
| --- | --- | --- |
| MainWindow | UI | Menu, controls, waveform, chunks grid, playback |
| RecordingOrchestrator | Orchestrator | Coordinates a session and raises UI events |
| AudioCaptureEngine | Service | Writes WAV chunks, rotates, queues conversion, raises peaks |
| FFmpegService | Integration | Background m4a conversion queue and m4a merge |
| FileManager | Service | Session folders, chunk paths, list/merge chunks |
| StateManager | Service | Persists session metadata as JSON |
| RecoveryManager | Service | Finds incomplete sessions on startup |
| LogManager | Service | Session log file and UI log event |
| ConfigurationManager | Service | Loads/saves/watches appsettings.json |
| MicrophoneCapture / LoopbackCapture | Strategy | The two capture modes |
| RecordingSession / AudioConfig / ChunkInfo | Model | Session, settings, and grid-row data |

## Keeping docs in sync

When the pipeline or UI changes, update `ARCHITECTURE.md`, `QUICK_REFERENCE.md`, and `workspace.dsl` together. The dynamic views in the DSL only reference relationships defined in the model, so add a relationship before using it in a view.
