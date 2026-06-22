---
title: Interview Recorder
---

# Interview Recorder

Windows desktop app for recording interviews to disk as chunked WAV, compressing each chunk to m4a in the background, and merging both into single files when recording stops.

## Documentation

- [Documentation index](docs/INDEX.md) - where to start.
- [Architecture](docs/ARCHITECTURE.md) - the authoritative design: capture, chunking, compression queue, stop/merge, threading, recovery.
- [Quick reference](docs/QUICK_REFERENCE.md) - flows, config, components.

## Architecture decision records

- [ADR 0001 - Record in fixed-length chunks](adrs/0001-record-in-fixed-length-chunks.md)
- [ADR 0002 - Compress with external FFmpeg](adrs/0002-compress-with-external-ffmpeg.md)

## Diagrams

- [Interactive C4 model](static/index.html) - the Structurizr export: context, container, component, and dynamic views.

## Source

- [GitHub repository](https://github.com/epoisson/InterviewRecorder)
