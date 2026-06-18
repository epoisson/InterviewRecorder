workspace "Interview Recorder" "C4 Model for Interview Audio Recorder Application" {

    model {
        # People
        user = person "User" "A person conducting or recording an interview" "User"
        
        # External Systems
        windowsOS = softwareSystem "Windows Operating System" "Provides audio device access and file system" "External System"
        ffmpeg = softwareSystem "FFmpeg" "External audio processing tool for compression and format conversion" "External System"
        
        # Main System
        interviewRecorder = softwareSystem "Interview Recorder" "Desktop application for recording and managing interview audio with crash recovery" {
            
            # Containers
            wpfApp = container "WPF Application" "Desktop UI application" "C# .NET 9, WPF" "Desktop Application" {

                # UI Components
                mainWindow = component "MainWindow" "Main UI window: controls, live waveform, in-app playback" "WPF Window" "UI"
                
                # Orchestration Layer
                recordingOrchestrator = component "RecordingOrchestrator" "Coordinates all recording operations and manages application state" "Service" "Orchestrator"
                
                # Core Services
                audioCaptureEngine = component "AudioCaptureEngine" "Writes WAV chunks from capture events, rotates chunks, queues each for conversion, raises peak levels" "Service" "Core"
                stateManager = component "StateManager" "Persists and recovers recording session state" "Service" "Core"
                fileManager = component "FileManager" "Manages file I/O operations and audio merging" "Service" "Core"
                recoveryManager = component "RecoveryManager" "Handles crash recovery and incomplete sessions" "Service" "Core"
                logManager = component "LogManager" "Manages application logging" "Service" "Core"
                configManager = component "ConfigurationManager" "Loads and manages application configuration" "Service" "Core"
                ffmpegService = component "FFmpegService" "Background queue converting chunks to m4a; merges m4a chunks on stop" "Service" "Integration"
                
                # Audio Capture Implementations
                microphoneCapture = component "MicrophoneCapture" "Captures audio from microphone" "IAudioCapture Implementation" "Audio"
                loopbackCapture = component "LoopbackCapture" "Captures system audio via loopback" "IAudioCapture Implementation" "Audio"
                mixedCapture = component "MixedCapture" "Captures both microphone and system audio" "IAudioCapture Implementation" "Audio"
                
                # Data Models
                recordingSession = component "RecordingSession" "Session state and metadata" "Data Model" "Model"
                audioConfig = component "AudioConfig" "Audio configuration settings" "Data Model" "Model"
                
                # Relationships - UI to Orchestrator
                mainWindow -> recordingOrchestrator "Uses" "Method calls, Events"
                
                # Relationships - Orchestrator to Services
                recordingOrchestrator -> audioCaptureEngine "Controls recording lifecycle" "Async methods"
                recordingOrchestrator -> stateManager "Persists session state" "Async methods"
                recordingOrchestrator -> fileManager "Creates directories" "Sync methods"
                recordingOrchestrator -> recoveryManager "Checks for incomplete sessions" "Async methods"
                recordingOrchestrator -> logManager "Logs events" "Async methods"
                recordingOrchestrator -> configManager "Reads configuration" "Sync properties"
                recordingOrchestrator -> ffmpegService "Manages compression" "Async methods"
                
                # Relationships - AudioCaptureEngine
                audioCaptureEngine -> microphoneCapture "Creates and controls" "Factory pattern"
                audioCaptureEngine -> loopbackCapture "Creates and controls" "Factory pattern"
                audioCaptureEngine -> mixedCapture "Creates and controls" "Factory pattern"
                audioCaptureEngine -> fileManager "Gets chunk paths, merges WAV chunks" "Direct calls"
                audioCaptureEngine -> ffmpegService "Queues each closed chunk for conversion" "Async calls"
                audioCaptureEngine -> recordingOrchestrator "Raises peak levels for the waveform" "AudioPeak event"
                
                # Relationships - FileManager
                fileManager -> recordingSession "Reads chunk list" "Property access"
                
                # Relationships - FFmpegService to FFmpeg
                ffmpegService -> ffmpeg "Converts chunks and concatenates m4a" "Process.Start"

                # Playback
                mainWindow -> windowsOS "Plays back the recording" "NAudio WaveOutEvent"
                
                # Relationships - StateManager
                stateManager -> fileManager "Gets metadata path" "Method calls"
                stateManager -> recordingSession "Serializes/Deserializes" "JSON"
                
                # Relationships - RecoveryManager
                recoveryManager -> fileManager "Scans directories" "Method calls"
                recoveryManager -> stateManager "Loads session state" "Method calls"
                
                # Relationships - ConfigManager
                configManager -> audioConfig "Deserializes to" "JSON"
                
                # NAudio Library Usage
                microphoneCapture -> windowsOS "Captures from device" "NAudio WaveInEvent"
                loopbackCapture -> windowsOS "Captures loopback" "NAudio WasapiLoopbackCapture"
                mixedCapture -> windowsOS "Captures both sources" "NAudio WaveInEvent + WasapiLoopbackCapture"

                # Audio delivery (used by the AudioDataFlow dynamic view)
                windowsOS -> microphoneCapture "Delivers captured audio" "NAudio callback"
                
                # Additional relationships for dynamic diagrams
                audioCaptureEngine -> windowsOS "Stops audio capture" "NAudio API"
                microphoneCapture -> audioCaptureEngine "Forwards audio (DataAvailable)" "Event"
                recordingOrchestrator -> mainWindow "Raises state, log and level events" "Events"
                recoveryManager -> recordingOrchestrator "Returns incomplete session" "Return value"
            }
            
            # File System Storage
            fileSystem = container "File System" "Stores recordings, session state, and logs" "Windows File System" "Storage" {
                recordingsFolder = component "Recordings Folder" "Root folder for all recordings" "Directory" "Storage"
                sessionFolder = component "Session Folder" "Individual session directory" "Directory" "Storage"
                chunksFolder = component "Chunks Folder" "Temporary audio chunk files" "Directory" "Storage"
                metadataFile = component "metadata.json" "Session state and metadata" "JSON File" "Storage"
                logFile = component "session.log" "Session event log" "Text File" "Storage"
                configFile = component "appsettings.json" "Application configuration" "JSON File" "Storage"
                audioChunks = component "Audio Chunk Files" "Raw WAV chunks" "WAV Files" "Storage"
                finalAudio = component "Final WAV File" "Merged interview recording" "WAV File" "Storage"
                compressedChunks = component "Compressed Chunk Files" "Per-chunk compressed audio" "m4a Files" "Storage"
                finalCompressed = component "Final m4a File" "Merged compressed recording" "m4a File" "Storage"
                
                # File System Relationships
                recordingsFolder -> sessionFolder "Contains multiple" "1:N"
                sessionFolder -> chunksFolder "Contains" "1:1"
                sessionFolder -> metadataFile "Contains" "1:1"
                sessionFolder -> logFile "Contains" "1:1"
                sessionFolder -> finalAudio "Contains (after merge)" "1:1"
                sessionFolder -> finalCompressed "Contains (after merge, if compression enabled)" "1:1"
                chunksFolder -> audioChunks "Contains multiple" "1:N"
                chunksFolder -> compressedChunks "Contains multiple (if enabled)" "1:N"
            }
            
            # External Configuration
            configStorage = container "Configuration Storage" "Application settings file" "JSON File" "Configuration"
            
            # Container Relationships
            wpfApp -> fileSystem "Reads/Writes" "File I/O"
            wpfApp -> configStorage "Reads configuration" "File I/O"
            wpfApp -> windowsOS "Accesses audio devices" "NAudio Library"
            wpfApp -> ffmpeg "Invokes for compression" "Process execution"
            
            # Additional relationships needed for dynamic diagrams
            fileManager -> fileSystem "Reads and writes files" "File I/O"
            ffmpeg -> fileSystem "Writes compressed files" "File I/O"
            audioCaptureEngine -> fileSystem "Writes chunk audio" "WaveFileWriter"
            ffmpegService -> fileSystem "Writes converted and merged files" "File I/O"
        }
        
        # System Context Relationships
        user -> interviewRecorder "Records interviews using" "WPF UI"
        interviewRecorder -> windowsOS "Uses audio devices from" "NAudio API"
        interviewRecorder -> ffmpeg "Compresses audio using" "Command-line execution"
        
        # Additional user interactions for dynamic diagrams
        user -> mainWindow "Interacts with" "Mouse/Keyboard"
        mainWindow -> user "Displays information to" "UI Updates"
        
        # Deployment
        deploymentEnvironment "Production" {
            deploymentNode "User's Computer" "" "Windows 10/11" {
                deploymentNode "Desktop Environment" "" "" {
                    softwareSystemInstance interviewRecorder
                }
                deploymentNode "Audio Hardware" "" "Audio devices" {
                    infrastructureNode microphone "Microphone" "Audio input device" "Hardware"
                    infrastructureNode speakers "Speakers/Headphones" "Audio output device" "Hardware"
                }
                deploymentNode "Local Storage" "" "NTFS" {
                    infrastructureNode myDocuments "My Documents" "User's Documents folder" "Storage"
                }
            }
        }
    }

    views {
        # System Context View (Level 1)
        systemContext interviewRecorder "SystemContext" {
            include *
            autoLayout lr
            description "System context diagram for Interview Recorder showing external dependencies"
        }
        
        # Container View (Level 2)
        container interviewRecorder "Containers" {
            include *
            autoLayout lr
            description "Container diagram showing the high-level technical building blocks"
        }
        
        # Component View - WPF Application (Level 3)
        component wpfApp "Components-Overview" {
            include *
            autoLayout tb
            description "Component diagram showing all components within the WPF application"
        }
        
        # Component View - Core Services
        component wpfApp "Components-CoreServices" {
            include mainWindow
            include recordingOrchestrator
            include audioCaptureEngine
            include stateManager
            include fileManager
            include recoveryManager
            include logManager
            include configManager
            include ffmpegService
            include recordingSession
            include audioConfig
            autoLayout lr
            description "Core service components and their relationships"
        }
        
        # Component View - Audio Capture
        component wpfApp "Components-AudioCapture" {
            include audioCaptureEngine
            include microphoneCapture
            include loopbackCapture
            include mixedCapture
            include fileManager
            include ffmpegService
            include windowsOS
            autoLayout tb
            description "Audio capture subsystem showing capture implementations"
        }
        
        # Component View - State Management
        component wpfApp "Components-StateManagement" {
            include recordingOrchestrator
            include stateManager
            include recoveryManager
            include fileManager
            include recordingSession
            autoLayout lr
            description "State management and crash recovery subsystem"
        }
        
        # Dynamic View - Start Recording
        dynamic wpfApp "StartRecording" "Start Recording Flow" {
            user -> mainWindow "1. Clicks Start Recording"
            mainWindow -> recordingOrchestrator "2. StartRecordingAsync()"
            recordingOrchestrator -> fileManager "3. CreateSessionDirectory()"
            recordingOrchestrator -> configManager "4. Get CurrentAudioConfig"
            recordingOrchestrator -> stateManager "5. SaveStateAsync()"
            recordingOrchestrator -> audioCaptureEngine "6. StartRecording()"
            audioCaptureEngine -> microphoneCapture "7. Create IAudioCapture"
            microphoneCapture -> windowsOS "8. Start capturing audio"
            audioCaptureEngine -> fileManager "9. Create first chunk file"
            recordingOrchestrator -> mainWindow "10. Raise StateChanged event"
            mainWindow -> user "11. Update UI (show recording)"
            autoLayout lr
            description "Sequence of operations when user starts recording"
        }
        
        # Dynamic View - Audio Data Flow
        dynamic wpfApp "AudioDataFlow" "Audio Data Processing Flow" {
            windowsOS -> microphoneCapture "1. Audio data available (real time)"
            microphoneCapture -> audioCaptureEngine "2. DataAvailable event"
            audioCaptureEngine -> fileSystem "3. Write bytes to current chunk"
            audioCaptureEngine -> recordingOrchestrator "4. Raise peak level (waveform)"
            audioCaptureEngine -> ffmpegService "5. On rotation: queue closed chunk"
            ffmpegService -> ffmpeg "6. Convert chunk to m4a"
            ffmpeg -> fileSystem "7. Write m4a chunk"
            autoLayout lr
            description "Continuous flow of audio data during recording"
        }
        
        # Dynamic View - Stop Recording
        dynamic wpfApp "StopRecording" "Stop Recording and Finalization" {
            user -> mainWindow "1. Clicks Stop Recording"
            mainWindow -> recordingOrchestrator "2. StopRecordingAsync()"
            recordingOrchestrator -> audioCaptureEngine "3. StopRecording() (final chunk queued)"
            audioCaptureEngine -> windowsOS "4. Stop audio capture"
            recordingOrchestrator -> ffmpegService "5. Stop() - drain conversion queue"
            ffmpegService -> ffmpeg "6. Convert remaining chunks"
            recordingOrchestrator -> fileManager "7. MergeChunksAsync() - merge WAV chunks"
            fileManager -> fileSystem "8. Write final WAV"
            recordingOrchestrator -> ffmpegService "9. MergeToM4a() - concat m4a chunks"
            ffmpegService -> fileSystem "10. Write final m4a"
            recordingOrchestrator -> stateManager "11. Mark session complete"
            recordingOrchestrator -> mainWindow "12. Return final file path"
            mainWindow -> user "13. Show completion dialog"
            autoLayout lr
            description "Sequence of operations when stopping and finalizing recording"
        }
        
        # Dynamic View - Crash Recovery
        dynamic wpfApp "CrashRecovery" "Crash Recovery Process" {
            mainWindow -> recordingOrchestrator "1. CheckForIncompleteSessionAsync()"
            recordingOrchestrator -> recoveryManager "2. FindIncompleteSessionAsync()"
            recoveryManager -> fileManager "3. Scan session directories"
            recoveryManager -> stateManager "4. Load session metadata"
            recoveryManager -> recordingOrchestrator "5. Return incomplete session"
            recordingOrchestrator -> mainWindow "6. Return session details"
            mainWindow -> user "7. Show recovery prompt"
            user -> mainWindow "8. User confirms recovery"
            mainWindow -> recordingOrchestrator "9. RecoverSessionAsync()"
            recordingOrchestrator -> audioCaptureEngine "10. InitializeForRecovery()"
            recordingOrchestrator -> mainWindow "11. Raise StateChanged"
            mainWindow -> user "12. Show recovered session"
            autoLayout lr
            description "Process of detecting and recovering from application crash"
        }
        
        # Deployment View
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
        
        # Themes
        theme default
    }
    
    # Documentation
    #!docs docs
    #!adrs adrs
}
