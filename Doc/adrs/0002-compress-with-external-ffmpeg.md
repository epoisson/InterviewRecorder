# 2. Compress with an external FFmpeg process

Date: 2026-06-19

## Status

Accepted

## Context

The app needs compressed (m4a) output but should stay lightweight and avoid
bundling/licensing a codec library.

## Decision

Invoke an external FFmpeg executable (path from config) on a background queue:
each closed chunk is converted to m4a, and the chunk m4a files are concatenated
into a single m4a on stop. FFmpeg is not bundled; it is discovered on `PATH`.

## Consequences

- No codec library dependency or licensing burden in the app itself.
- Compression is skipped (with a warning) if FFmpeg is not installed.
- Conversion runs off the capture path, so it never blocks recording.
