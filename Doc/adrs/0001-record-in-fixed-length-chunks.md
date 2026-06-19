# 1. Record audio in fixed-length chunks

Date: 2026-06-19

## Status

Accepted

## Context

A long recording held in a single open file is lost entirely if the application
or machine crashes mid-session, and cannot be compressed until recording ends.

## Decision

Write audio to fixed-length WAV chunks (`chunks/chunk_NNNN.wav`, length set by
`ChunkDurationMinutes`). Rotate to a new chunk on the boundary, and merge all
chunks into the final WAV (and m4a) when recording stops.

## Consequences

- A crash loses at most the in-progress chunk; recovery rebuilds the chunk list
  from disk and can resume at the next index.
- Each closed chunk can be compressed in the background while recording continues.
- Slightly more files on disk and a merge step at stop.
