workspace "Interview Recorder" "C4 Model for Interview Audio Recorder Application" {

    # Relationships are declared explicitly at each level (component + container), so don't
    # auto-create implied ones (which would collide with the explicit container relationships).
    !impliedRelationships false

    model {
        # People
        user = person "User" "A person conducting or recording an interview" "User"

        # External Systems
        windowsOS = softwareSystem "Windows Operating System" "Provides audio device access and file system" "External System"
        ffmpeg = softwareSystem "FFmpeg" "External audio processing tool for compression and format conversion" "External System"

        # Main System
        interviewRecorder = softwareSystem "Interview Recorder" "Desktop application for recording and managing interview audio with crash recovery" {

            !docs docs
            !adrs adrs

            # ---- Container: WPF UI ----
            app = container "Desktop UI" "WPF front-end: menu, controls, live waveform, chunks grid, in-app playback" "C# .NET 9, WPF" "Desktop Application" {
                mainWindow = component "MainWindow" "Main UI window; binds to IRecorder and marshals events to the dispatcher" "WPF Window" "UI"
            }

            # ---- Container: reusable recording core (no UI dependency) ----
            core = container "Recording Core" "Reusable recording engine, capture modes, persistence and configuration" "C# .NET 9 class library" "Library" {
                # Central coordinator
                recordingOrchestrator = component "RecordingOrchestrator" "Coordinates a session and raises UI events; implements IRecorder" "Service" "Orchestrator"

                group "Contracts" {
                    irecorder = component "IRecorder" "Recording surface the UI binds to: start/pause/resume/stop, recovery, status and events" "C# interface" "Contract"
                    iaudiocapture = component "IAudioCapture" "Capture-source abstraction: raises DataAvailable and exposes the capture format" "C# interface" "Contract"
                }

                group "Capture" {
                    audioCaptureEngine = component "AudioCaptureEngine" "Writes WAV chunks from capture events, rotates chunks, queues each for conversion, raises peak levels" "Service" "Core"
                    microphoneCapture = component "MicrophoneCapture" "Captures audio from microphone" "IAudioCapture Implementation" "Audio"
                    loopbackCapture = component "LoopbackCapture" "Captures system audio via loopback" "IAudioCapture Implementation" "Audio"
                }

                group "Compression" {
                    ffmpegService = component "FFmpegService" "Background queue converting chunks to m4a; merges m4a chunks on stop" "Service" "Integration"
                }

                group "Persistence & recovery" {
                    fileManager = component "FileManager" "File I/O: session folders, chunk paths, listing and merging WAVs" "Service" "Core"
                    stateManager = component "StateManager" "Persists and recovers recording session state" "Service" "Core"
                    recoveryManager = component "RecoveryManager" "Finds and loads incomplete sessions" "Service" "Core"
                }

                group "Configuration & logging" {
                    configManager = component "ConfigurationManager" "Loads, validates, watches and saves configuration" "Service" "Core"
                    logManager = component "LogManager" "Session logging" "Service" "Core"
                }

                group "Models" {
                    recordingSession = component "RecordingSession" "Session state and metadata" "Data Model" "Model"
                    audioConfig = component "AudioConfig" "Audio configuration settings" "Data Model" "Model"
                }

                # Contract realisation
                recordingOrchestrator -> irecorder "Implements" "C# interface"

                # Orchestrator -> services
                recordingOrchestrator -> audioCaptureEngine "Controls recording lifecycle" "Async methods"
                recordingOrchestrator -> stateManager "Persists session state" "Async methods"
                recordingOrchestrator -> fileManager "Creates dirs, lists chunks, merges WAVs" "Methods"
                recordingOrchestrator -> recoveryManager "Finds incomplete sessions" "Async methods"
                recordingOrchestrator -> logManager "Logs events" "Async methods"
                recordingOrchestrator -> configManager "Reads configuration" "Properties"
                recordingOrchestrator -> ffmpegService "Manages and drains conversion" "Async methods"

                # Capture engine
                audioCaptureEngine -> iaudiocapture "Creates and controls (per capture mode)" "Factory"
                microphoneCapture -> iaudiocapture "Implements" "C# interface"
                loopbackCapture -> iaudiocapture "Implements" "C# interface"
                iaudiocapture -> audioCaptureEngine "Raises DataAvailable / RecordingStopped" "Events"
                audioCaptureEngine -> fileManager "Gets chunk paths" "Direct calls"
                audioCaptureEngine -> ffmpegService "Queues each closed chunk" "Async calls"
                audioCaptureEngine -> recordingOrchestrator "Raises peak/chunk events" "Events"

                # Persistence / recovery / config
                fileManager -> recordingSession "Reads chunk list" "Property access"
                stateManager -> fileManager "Gets metadata path" "Method calls"
                stateManager -> recordingSession "Serializes/Deserializes" "JSON"
                recoveryManager -> fileManager "Scans directories" "Method calls"
                recoveryManager -> stateManager "Loads session state" "Method calls"
                recoveryManager -> recordingOrchestrator "Returns incomplete session" "Return value"
                configManager -> audioConfig "Deserializes to" "JSON"
            }

            # ---- Storage containers ----
            fileSystem = container "File System" "Stores chunk + merged WAV/m4a files, metadata.json and session.log under Documents\InterviewRecordings" "Windows File System" "Storage"
            configStorage = container "Configuration Storage" "appsettings.json (capture, chunking, compression settings)" "JSON File" "Configuration"

            # ---- Component-level cross-container / external relationships ----
            mainWindow -> irecorder "Drives recording (start/pause/resume/stop, recover)" "Method calls"
            recordingOrchestrator -> mainWindow "Raises state, log, level, chunk and conversion events" "Events"
            mainWindow -> windowsOS "Plays back the recording" "NAudio WaveOutEvent"
            mainWindow -> fileSystem "Reads the merged file for playback" "File read"

            microphoneCapture -> windowsOS "Captures from device" "NAudio WaveInEvent"
            loopbackCapture -> windowsOS "Captures loopback" "NAudio WasapiLoopbackCapture"
            windowsOS -> iaudiocapture "Delivers captured audio" "NAudio callback"
            audioCaptureEngine -> windowsOS "Stops audio capture" "NAudio API"
            ffmpegService -> ffmpeg "Converts chunks and concatenates m4a" "Process.Start"

            audioCaptureEngine -> fileSystem "Writes chunk audio" "WaveFileWriter"
            fileManager -> fileSystem "Reads and writes files" "File I/O"
            stateManager -> fileSystem "Writes metadata.json" "File I/O"
            logManager -> fileSystem "Writes session.log" "File I/O"
            ffmpegService -> fileSystem "Writes converted and merged files" "File I/O"
            ffmpeg -> fileSystem "Writes compressed files" "File I/O"
            configManager -> configStorage "Reads, saves and watches settings" "File I/O + FileSystemWatcher"

            # ---- Container-level relationships (for the Container and dynamic views) ----
            user -> app "Records / plays back interviews" "Mouse / keyboard"
            app -> user "Displays status, waveform and chunks" "UI"
            app -> core "Drives recording via IRecorder" "Method calls"
            core -> app "Raises state / level / chunk events" "Events"
            app -> windowsOS "Plays back recordings" "NAudio WaveOutEvent"
            app -> fileSystem "Reads merged recording for playback" "File read"
            core -> windowsOS "Captures and stops audio" "NAudio (WaveIn / WASAPI)"
            windowsOS -> core "Delivers captured audio" "NAudio callbacks"
            core -> ffmpeg "Converts and merges audio" "Process execution"
            core -> fileSystem "Reads and writes recordings" "File I/O"
            core -> configStorage "Reads, saves and watches settings" "File I/O"
        }

        # System Context
        user -> interviewRecorder "Records interviews using" "WPF UI"
        interviewRecorder -> windowsOS "Uses audio devices from" "NAudio API"
        interviewRecorder -> ffmpeg "Compresses audio using" "Command-line execution"

        # Deployment
        deploymentEnvironment "Production" {
            deploymentNode "User's Computer" "The end user's Windows PC" "Windows 10/11" {
                deploymentNode "Desktop Runtime" "Hosts the WPF application" ".NET 9 Desktop Runtime" {
                    recorderInstance = softwareSystemInstance interviewRecorder
                }
                deploymentNode "Audio Hardware" "Local audio devices" "Windows audio stack" {
                    microphone = infrastructureNode "Microphone" "Audio input device" "Audio hardware" "Hardware"
                    speakers = infrastructureNode "Speakers / Headphones" "Audio output device" "Audio hardware" "Hardware"
                }
                deploymentNode "Local Storage" "Where recordings are written" "NTFS volume" {
                    myDocuments = infrastructureNode "My Documents" "User's Documents folder (InterviewRecordings)" "NTFS" "Storage"
                }
            }

            recorderInstance -> microphone "Captures from" "WASAPI / WaveIn"
            recorderInstance -> speakers "Plays back to" "WaveOut"
            recorderInstance -> myDocuments "Reads and writes recordings" "File I/O"
        }
    }

    views {
        # Level 0 - landscape
        systemLandscape "Landscape" {
            include *
            autoLayout lr
            description "The recorder, its user, and external dependencies (Windows, FFmpeg)."
        }

        # Level 1 - context
        systemContext interviewRecorder "SystemContext" {
            include *
            autoLayout lr
            description "System context: external dependencies of the Interview Recorder."
        }

        # Level 2 - containers
        container interviewRecorder "Containers" {
            include *
            autoLayout lr
            description "WPF UI over a reusable recording core, with file-system and config storage."
        }

        # Level 3 - components (grouped overview + focused slices to keep each diagram readable)
        component core "Components-Core" {
            include *
            autoLayout lr
            description "Recording core, grouped by responsibility (contracts, capture, compression, persistence, config, models)."
        }

        # Focused slice: the capture + conversion pipeline
        component core "Components-Capture" {
            include audioCaptureEngine iaudiocapture microphoneCapture loopbackCapture ffmpegService recordingOrchestrator windowsOS ffmpeg fileSystem
            autoLayout lr
            description "Capture pipeline: engine, IAudioCapture implementations, conversion and where audio is written."
        }

        # Focused slice: session lifecycle, persistence and recovery
        component core "Components-Session" {
            include recordingOrchestrator irecorder stateManager fileManager recoveryManager configManager logManager recordingSession audioConfig fileSystem configStorage
            autoLayout lr
            description "Session coordination, persistence, recovery, configuration and logging."
        }

        component app "Components-App" {
            include *
            autoLayout lr
            description "The WPF UI and its dependency on the IRecorder contract."
        }

        # Dynamic views (container level, since flows span the UI and the core)
        dynamic interviewRecorder "StartRecording" "Start Recording" {
            user -> app "1. Click Start Recording"
            app -> core "2. StartRecordingAsync()"
            core -> windowsOS "3. Start capturing audio"
            core -> fileSystem "4. Create first chunk"
            app -> user "5. Show recording state"
            autoLayout lr
        }

        dynamic interviewRecorder "Pause" "Pause Recording" {
            user -> app "1. Click Pause"
            app -> core "2. PauseRecordingAsync()"
            core -> windowsOS "3. Stop capture; close current chunk"
            core -> ffmpeg "4. Queue closed chunk for conversion"
            app -> user "5. Show paused"
            autoLayout lr
        }

        dynamic interviewRecorder "Resume" "Resume Recording" {
            user -> app "1. Click Resume"
            app -> core "2. ResumeRecordingAsync()"
            core -> fileSystem "3. Open the next chunk"
            core -> windowsOS "4. Restart capture"
            app -> user "5. Show recording state"
            autoLayout lr
        }

        dynamic interviewRecorder "AudioDataFlow" "Audio Data Flow" {
            windowsOS -> core "1. Audio data available (real time)"
            core -> fileSystem "2. Write bytes to current chunk"
            core -> app "3. Raise peak level (waveform)"
            core -> ffmpeg "4. On rotation: convert closed chunk"
            ffmpeg -> fileSystem "5. Write m4a chunk"
            autoLayout lr
        }

        dynamic interviewRecorder "StopRecording" "Stop and Finalize" {
            user -> app "1. Click Stop"
            app -> core "2. StopRecordingAsync()"
            core -> windowsOS "3. Stop audio capture"
            core -> ffmpeg "4. Drain conversions"
            core -> fileSystem "5. Merge WAV + write final m4a"
            app -> user "6. Show completion"
            autoLayout lr
        }

        dynamic interviewRecorder "Play" "Play Recording" {
            user -> app "1. Click Play"
            app -> fileSystem "2. Read the merged WAV"
            app -> windowsOS "3. Play audio and meter the waveform"
            app -> user "4. Show playback / live waveform"
            autoLayout lr
        }

        dynamic interviewRecorder "CrashRecovery" "Crash Recovery" {
            app -> core "1. CheckForIncompleteSession / RecoverSession"
            core -> fileSystem "2. Scan dirs + list existing chunks"
            core -> ffmpeg "3. Re-queue chunks without an m4a"
            core -> app "4. Raise StateChanged (paused)"
            app -> user "5. Show recovered session (paused)"
            autoLayout lr
            description "Reopen a crashed session paused; resume continues at the next chunk, stop merges existing chunks."
        }

        # Deployment
        deployment interviewRecorder "Production" "Deployment" {
            include *
            autoLayout lr
            description "Production deployment architecture"
        }

        # Styling
        styles {
            element "Software System" {
                background #1168bd
                color #ffffff
                shape RoundedBox
            }
            element "External System" {
                background #999999
                color #ffffff
                shape RoundedBox
            }
            element "Container" {
                background #438dd5
                color #ffffff
                shape RoundedBox
            }
            element "Desktop Application" {
                shape Window
            }
            element "Library" {
                background #2f6f4f
                color #ffffff
                shape RoundedBox
            }
            element "Storage" {
                background #85bbf0
                color #000000
                shape Cylinder
            }
            element "Configuration" {
                background #ffa500
                color #000000
                shape Folder
            }
            element "Component" {
                background #85bbf0
                color #000000
                shape Component
            }
            element "Contract" {
                background #bdb2ff
                color #000000
                shape Component
            }
            element "UI" {
                background #b3d9ff
                color #000000
                shape Component
            }
            element "Orchestrator" {
                background #ff6b6b
                color #ffffff
                shape Hexagon
            }
            element "Core" {
                background #4ecdc4
                color #000000
                shape Component
            }
            element "Integration" {
                background #ffe66d
                color #000000
                shape Component
            }
            element "Audio" {
                background #95e1d3
                color #000000
                shape Component
            }
            element "Model" {
                background #f38181
                color #000000
                shape Box
            }
            element "Person" {
                background #08427b
                color #ffffff
                shape Person
            }
            element "User" {
                background #08427b
                color #ffffff
                shape Person
            }
            element "Hardware" {
                background #666666
                color #ffffff
                shape Box
            }
        }

        theme default
    }
}
