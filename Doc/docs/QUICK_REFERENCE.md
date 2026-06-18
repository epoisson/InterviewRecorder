# Interview Recorder - Quick Reference

## System Architecture at a Glance

```
┌─────────────┐
│   User      │
└──────┬──────┘
       │
┌──────▼──────────────────────────────────────────────────────┐
│                    MainWindow (WPF)                         │
└──────┬──────────────────────────────────────────────────────┘
       │
┌──────▼────────────────────────────────────────────────────┐
│              RecordingOrchestrator                        │
│  (Coordinates all services, manages lifecycle)            │
└───┬────┬────┬────┬────┬────┬────┬─────────────────────────┘
    │    │    │    │    │    │    │
    ▼    ▼    ▼    ▼    ▼    ▼    ▼
  ┌─┴─┐┌─┴─┐┌─┴─┐┌─┴─┐┌─┴─┐┌─┴─┐┌─┴────┐
  │ACE││SM ││FM ││RM ││LM ││CM ││FFmpeg│
  └───┘└───┘└───┘└───┘└───┘└───┘└──────┘
    │
    ▼
┌──────────────────────────────────┐
│   IAudioCapture (Strategy)       │
├──────────────────────────────────┤
│ • MicrophoneCapture              │
│ • LoopbackCapture                │
│ • MixedCapture                   │
└──────────────────────────────────┘
```

**Legend**:
- ACE = AudioCaptureEngine
- SM = StateManager
- FM = FileManager
- RM = RecoveryManager
- LM = LogManager
- CM = ConfigurationManager

## Key Workflows

### Recording Start Flow
```
User → MainWindow.StartButton_Click()
  → RecordingOrchestrator.StartRecordingAsync()
    → Create RecordingSession
    → StateManager.SaveStateAsync()
    → AudioCaptureEngine.StartRecording()
      → Create IAudioCapture (factory)
      → Start audio capture
      → Create first chunk file
    → Start duration timer
    → Raise StateChanged event
  → MainWindow updates UI
```

### Audio Data Flow
```
Audio Device → IAudioCapture.DataAvailable
  → AudioCaptureEngine.OnDataAvailable()
    → Write to chunk file
    → Check chunk duration (every 5 min)
      → Close current chunk
      → Queue for FFmpeg compression
      → Create new chunk
    → Check auto-save interval (every 30 sec)
      → StateManager.SaveStateAsync()
```

### Recording Stop Flow
```
User → MainWindow.StopButton_Click()
  → RecordingOrchestrator.StopRecordingAsync()
    → AudioCaptureEngine.StopRecording()
    → Stop duration timer
    → FFmpegService.Stop() (wait for queue)
    → FileManager.MergeChunksAsync()
      → Read all chunk files
      → Write merged WAV file
    → StateManager.MarkSessionCompleteAsync()
    → Return final file path
  → MainWindow shows completion dialog
```

### Crash Recovery Flow
```
Application Startup
  → MainWindow.CheckForRecovery()
    → RecordingOrchestrator.CheckForIncompleteSessionAsync()
      → RecoveryManager.FindIncompleteSessionAsync()
        → Scan session directories
        → Load metadata.json files
        → Find sessions where State != Completed
      → Return most recent incomplete session
    → Show recovery dialog to user
    → If user confirms:
      → RecordingOrchestrator.RecoverSessionAsync()
        → Load session state
        → Initialize AudioCaptureEngine
        → Update UI
```

## State Machine

```
     ┌──────┐
     │ Idle │◄────────────────┐
     └───┬──┘                 │
         │ Start              │
         ▼                    │
   ┌───────────┐         ┌────────────┐
   │ Recording │────────►│ Completed  │
   └─┬─────────┘   Stop  └────────────┘
     │     ▲                
Pause│     |Resume          
     ▼     │                
   ┌────────┐                  
   │ Paused |                
   └────────┘                          
```

## File Structure

```
My Documents/
└── InterviewRecordings/
    ├── abc123-session-id/
    │   ├── chunks/
    │   │   ├── chunk_0000.wav
    │   │   ├── chunk_0000.mp3
    │   │   ├── chunk_0001.wav
    │   │   ├── chunk_0001.mp3
    │   │   └── ...
    │   ├── metadata.json         # Session state
    │   ├── session.log           # Event log
    │   └── interview_20240127_123456.wav  # Final merged file
    └── def456-session-id/
        └── ...
```

## Component Responsibilities

| Component | Primary Responsibility | Key Methods |
|-----------|----------------------|-------------|
| **MainWindow** | UI and user interaction | Button handlers, UI updates |
| **RecordingOrchestrator** | Coordinate services | Start/Pause/Resume/Stop Recording |
| **AudioCaptureEngine** | Audio lifecycle | Create chunks, manage capture |
| **StateManager** | Persist state | SaveState, LoadState |
| **FileManager** | File operations | Create directories, merge chunks |
| **RecoveryManager** | Crash recovery | Find incomplete sessions |
| **LogManager** | Logging | Write to session log |
| **ConfigurationManager** | Configuration | Load settings |
| **FFmpegService** | Compression | Queue compression jobs |
| **MicrophoneCapture** | Mic recording | Capture from input device |
| **LoopbackCapture** | System audio | Capture loopback |
| **MixedCapture** | Both sources | Mix mic + system audio |

## Configuration Quick Reference

```json
{
  "AudioConfig": {
    "CaptureMode": "InputDevice | Loopback | Mix",
    "SampleRate": 44100,          // Hz
    "Channels": 1,                // 1=mono, 2=stereo
    "BitsPerSample": 16,          // 16 or 24
    "ChunkDurationMinutes": 5,    // 1-60
    "AutoSaveIntervalSeconds": 30, // 10-300
    "Compression": {
      "Enabled": true,
      "Format": "mp3",            // mp3, aac, ogg
      "Bitrate": "128k",          // 64k-320k
      "FFmpegPath": "ffmpeg"
    }
  }
}
```

## Common Operations

### Get Current Status
```csharp
var state = _orchestrator.CurrentState;
var duration = _orchestrator.CurrentDuration;
var sessionId = _orchestrator.CurrentSessionId;
```

### Start Recording
```csharp
await _orchestrator.StartRecordingAsync();
```

### Pause/Resume
```csharp
await _orchestrator.PauseRecordingAsync();
await _orchestrator.ResumeRecordingAsync();
```

### Stop and Finalize
```csharp
var finalFile = await _orchestrator.StopRecordingAsync();
```

### Check for Recovery
```csharp
var incomplete = await _orchestrator.CheckForIncompleteSessionAsync();
if (incomplete != null)
{
    await _orchestrator.RecoverSessionAsync(incomplete.SessionId);
}
```

## Event Subscriptions

```csharp
// In MainWindow constructor
_orchestrator.LogMessage += OnLogMessage;
_orchestrator.StateChanged += OnStateChanged;

// Handlers
private void OnLogMessage(string message)
{
    Dispatcher.Invoke(() => LogTextBox.AppendText($"[{DateTime.Now}] {message}\n"));
}

private void OnStateChanged()
{
    Dispatcher.Invoke(() => UpdateUIState());
}
```

## Threading Model

| Component | Thread | Notes |
|-----------|--------|-------|
| MainWindow | UI Thread | Must use Dispatcher for cross-thread access |
| RecordingOrchestrator | UI Thread | Async methods, timer on background thread |
| AudioCaptureEngine | Background | Audio callbacks on NAudio thread |
| StateManager | Background | All operations async |
| FileManager | Background | Async I/O |
| FFmpegService | Background | Queue processor on background task |

## Memory Considerations

- **Chunk Size**: 5 min @ 44.1kHz = ~50 MB WAV
- **Compression**: 10:1 ratio typical (MP3 128k)
- **Auto-save**: Minimal overhead (JSON serialization)
- **Peak Memory**: ~100 MB during merge operation

## Performance Characteristics

| Operation | Time Complexity | Notes |
|-----------|----------------|-------|
| Start Recording | O(1) | Instant |
| Audio Capture | O(1) | Constant per buffer |
| Chunk Creation | O(1) | New file every N minutes |
| Compression | O(n) | Background, non-blocking |
| State Save | O(1) | JSON serialization |
| Stop Recording | O(n) | Merge all chunks |
| Recovery Scan | O(m) | m = number of sessions |

## Debugging Tips

### Enable Verbose Logging
```csharp
// In LogManager
LogLevel = LogLevel.Verbose;
```

### Check Session State
```
[SessionId]/metadata.json
```

### View Chunks
```
[SessionId]/chunks/
```

### Check FFmpeg Output
```
session.log (contains FFmpeg stderr)
```

### Verify Audio Format
```csharp
using var reader = new WaveFileReader(chunkPath);
Console.WriteLine($"Format: {reader.WaveFormat}");
```

## Testing Checklist

- [ ] Start recording - verify status and duration
- [ ] Pause recording - verify duration stops
- [ ] Resume recording - verify duration continues
- [ ] Stop recording - verify final file created
- [ ] Check file size - verify compression works
- [ ] Simulate crash - verify recovery prompt
- [ ] Test different capture modes
- [ ] Test with different configurations
- [ ] Test long recording (30+ minutes)
- [ ] Test disk space handling

## Troubleshooting Quick Guide

| Problem | Check | Solution |
|---------|-------|----------|
| Buttons not responding | _orchestrator initialized? | Use async initialization pattern |
| Duration not updating | Timer started? | Uncomment InitializeTimer() |
| No audio captured | Device available? | Check audio devices |
| Compression not working | FFmpeg installed? | Install FFmpeg or disable compression |
| Recovery not working | metadata.json exists? | Check file permissions |
| High CPU usage | Compression enabled? | Reduce bitrate or disable |

## Key Files to Know

| File | Purpose | Location |
|------|---------|----------|
| MainWindow.xaml.cs | UI logic | Root |
| RecordingOrchestrator.cs | Orchestration | Services/ |
| AudioCaptureEngine.cs | Audio management | Services/ |
| appsettings.json | Configuration | App directory |
| metadata.json | Session state | Per session |
| session.log | Event log | Per session |

## Quick Commands

### Build
```bash
dotnet build
```

### Run
```bash
dotnet run
```

### Publish
```bash
dotnet publish -c Release -r win-x64 --self-contained
```

### Test Configuration
```bash
# Edit appsettings.json
# Restart application
```

## Version Information

- **.NET**: 8.0
- **NAudio**: 2.2.1+
- **Target**: Windows 10+
- **Architecture**: x64

## Contact and Support

- **Documentation**: See docs/ folder
- **Architecture**: See workspace.dsl
- **Issues**: Check session.log first
- **Configuration**: Edit appsettings.json
