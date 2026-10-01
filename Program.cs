using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Raylib_cs;
using Color = Raylib_cs.Color;

namespace O2Play
{
    public class O2Game
    {
        public const string MutexName = IpcManager.MutexName;
        public const string PipeName = IpcManager.PipeName;

        public static void ForceForegroundWindow(IntPtr hWnd) => IpcManager.ForceForegroundWindow(hWnd);
        public static bool AllowSetForegroundWindow(int dwProcessId) => IpcManager.AllowSetForegroundWindow(dwProcessId);

        private BmsChart _chart = null!;
        private AudioEngine _audio = null!;
        private GameEngine _engine = null!;

        private volatile string? _pendingFileToLoad = null;
        private volatile int _pendingDifficultyToLoad = -1;
        private bool _pausedDueToBackground = false;
        private bool _exitRequested = false;

        private readonly ConcurrentQueue<string[]> _pendingIpcCommands = new();
        private CancellationTokenSource? _ipcCancelSource;

        private bool _cmdPlay = false;
        private int _cmdMeasure = 0;

        private void StartIpcServer()
        {
            _ipcCancelSource = new CancellationTokenSource();
            IpcManager.StartIpcServer(args => _pendingIpcCommands.Enqueue(args), _ipcCancelSource.Token);
        }

        public void ProcessCommandLineArgs(string[] args, bool isInitial = false)
        {
            if (args.Length == 0)
            {
                if (!isInitial)
                {
                    ForceForegroundWindow(GetHwnd());
                }
                return;
            }

            var parsed = CommandLineParser.Parse(args);

            if (parsed.IsStop)
            {
                if (isInitial)
                {
                    _exitRequested = true;
                    return;
                }

                _pausedDueToBackground = false;
                if (_engine != null)
                {
                    _engine.IsPlaying = false;
                    _engine.Audio.StopAll();
                    _engine.Audio.StopBgm();
                    _engine.SeekTo(0);
                }
                return;
            }

            if (parsed.FilePath != null)
            {
                _pendingFileToLoad = parsed.FilePath;
                _pendingDifficultyToLoad = parsed.Difficulty.HasValue ? (int)parsed.Difficulty.Value : -1;
                _cmdPlay = parsed.IsPlay;
                _cmdMeasure = parsed.Measure;
                if (!isInitial)
                {
                    ForceForegroundWindow(GetHwnd());
                }
            }
            else if (parsed.IsPlay)
            {
                _pausedDueToBackground = false;
                if (_engine != null)
                {
                    _engine.IsPlaying = true;
                    if (parsed.Measure > 0 && !_chart.IsDummy)
                    {
                        double targetTime = _chart.TickToSeconds(parsed.Measure * 192.0);
                        _engine.SeekTo(targetTime);
                    }
                    _engine.Audio.Resume();
                }
                if (!isInitial)
                {
                    ForceForegroundWindow(GetHwnd());
                }
            }
            else if (!isInitial)
            {
                ForceForegroundWindow(GetHwnd());
            }
        }

        public void Run(string[] initialArgs)
        {
            StartIpcServer();

            // Hardware-accelerated OpenGL with VSync locked to native display refresh rate
            Raylib.SetConfigFlags(ConfigFlags.VSyncHint);
            Raylib.InitWindow(391, 600, "O2Viewer");
            Raylib.SetExitKey(KeyboardKey.Null); // GameEngine handles Escape when window is focused
            Raylib.SetTargetFPS(0); // 0 lets OpenGL VSync buffer swaps govern cadence without CPU timer jitter

            _audio = new AudioEngine();
            _chart = ChartManager.GenerateDummyChart();

            string initTitle = !string.IsNullOrEmpty(_chart.Header.Title) && _chart.Header.Title != "O2Viewer" ? $"O2Viewer - {_chart.Header.Title}" : "O2Viewer";
            Raylib.SetWindowTitle(initTitle);

            _engine = new GameEngine(_chart, _audio);
            _engine.RequestOpenFile += OpenBmsDialog;
            _engine.RequestChangeDifficulty += ChangeOjnDifficulty;

            if (initialArgs.Length > 0)
            {
                ProcessCommandLineArgs(initialArgs, isInitial: true);
                if (_exitRequested)
                {
                    _audio.StopAll();
                    _engine?.Dispose();
                    Raylib.CloseWindow();
                    return;
                }
            }

            int _testFrameCounter = 0;
            while (!Raylib.WindowShouldClose())
            {
                // Dequeue and process any commands sent by iBMSC or subsequent instances
                while (_pendingIpcCommands.TryDequeue(out var ipcArgs))
                {
                    ProcessCommandLineArgs(ipcArgs, isInitial: false);
                }

                // Only exit on Escape when window is focused
                if (Raylib.IsWindowFocused() && Raylib.IsKeyPressed(KeyboardKey.Escape))
                {
                    break;
                }

                // File Drag and Drop directly onto the Raylib OpenGL window
                if (Raylib.IsFileDropped())
                {
                    var dropped = Raylib.LoadDroppedFiles();
                    unsafe
                    {
                        for (int i = 0; i < (int)dropped.Count; i++)
                        {
                            if (dropped.Paths != null && dropped.Paths[i] != null)
                            {
                                string? file = Marshal.PtrToStringUTF8((IntPtr)dropped.Paths[i]);
                                if (!string.IsNullOrEmpty(file) &&
                                    (file.EndsWith(".bms", StringComparison.OrdinalIgnoreCase) ||
                                     file.EndsWith(".bme", StringComparison.OrdinalIgnoreCase) ||
                                     file.EndsWith(".bml", StringComparison.OrdinalIgnoreCase) ||
                                     file.EndsWith(".ojn", StringComparison.OrdinalIgnoreCase)))
                                {
                                    _pendingFileToLoad = file;
                                    break;
                                }
                            }
                        }
                    }
                    Raylib.UnloadDroppedFiles(dropped);
                }

                // Process any pending file chosen via dialog, drag & drop, or command line
                if (_pendingFileToLoad != null)
                {
                    string file = _pendingFileToLoad;
                    OjnDifficulty? diff = _pendingDifficultyToLoad >= 0 ? (OjnDifficulty)_pendingDifficultyToLoad : null;
                    _pendingFileToLoad = null;
                    _pendingDifficultyToLoad = -1;
                    LoadBmsFile(file, diff);
                }

                // Background audio management: if on background or minimized, don't play audio
                bool isWindowActive = Raylib.IsWindowFocused() && !Raylib.IsWindowMinimized();

                if (!isWindowActive)
                {
                    if (_engine.IsPlaying && !_pausedDueToBackground)
                    {
                        _pausedDueToBackground = true;
                        _engine.IsPlaying = false;
                        _audio.Pause();
                    }
                }
                else
                {
                    if (_pausedDueToBackground)
                    {
                        _pausedDueToBackground = false;
                        _engine.IsPlaying = true;
                        _audio.Resume();
                    }
                }

                float dt = Math.Min(Raylib.GetFrameTime(), 0.1f);
                _audio.Update();
                _engine.IsWindowFocused = isWindowActive;
                _engine.Update(dt);

                Raylib.BeginDrawing();
                Raylib.ClearBackground(new Color(0, 96, 128, 255));
                _engine.Draw();
                Raylib.EndDrawing();

                string? testExitEnv = Environment.GetEnvironmentVariable("O2_TEST_EXIT_FRAMES");
                if (testExitEnv != null && int.TryParse(testExitEnv, out int maxFrames) && ++_testFrameCounter >= maxFrames)
                {
                    break;
                }
            }

            _ipcCancelSource?.Cancel();
            _audio.StopAll();
            _engine?.Dispose();
            Raylib.CloseWindow();
        }

        private static unsafe IntPtr GetHwnd() => (IntPtr)Raylib.GetWindowHandle();

        public void OpenBmsDialog()
        {
            SongLoader.OpenSongDialog(GetHwnd(), file => _pendingFileToLoad = file);
        }

        public void LoadBmsFile(string filePath, OjnDifficulty? preferredDifficulty = null)
        {
            try
            {
                _pausedDueToBackground = false;
                if (_engine != null) _engine.IsPlaying = false;
                _audio.StopAll();
                _audio.StopBgm();

                IntPtr hwnd = GetHwnd();
                var loadedChart = SongLoader.LoadSong(filePath, _audio, preferredDifficulty, hwnd);
                if (loadedChart == null) return;

                _chart = loadedChart;

                string songTitle = !string.IsNullOrEmpty(_chart.Header.Title) ? _chart.Header.Title : Path.GetFileNameWithoutExtension(filePath);
                string diffSuffix = (_chart.IsOjn && _chart.CurrentDifficulty.HasValue) ? $" [{_chart.CurrentDifficulty}]" : "";
                Raylib.SetWindowTitle($"O2Viewer - {songTitle}{diffSuffix}");

                _engine?.LoadChart(_chart);

                bool isWindowActive = Raylib.IsWindowFocused() && !Raylib.IsWindowMinimized();
                if (isWindowActive)
                {
                    _pausedDueToBackground = false;
                    if (_engine != null)
                    {
                        _engine.IsPlaying = true;
                        _audio.Resume();
                    }
                }
                else
                {
                    _pausedDueToBackground = true;
                    if (_engine != null) _engine.IsPlaying = false;
                    _audio.Pause();
                }

                // Auto-play from command-line (-P -N<measure>)
                if (_cmdPlay && _engine != null)
                {
                    _pausedDueToBackground = false;
                    _engine.IsPlaying = true;
                    if (_cmdMeasure > 0)
                    {
                        double targetTime = _chart.TickToSeconds(_cmdMeasure * 192.0);
                        _engine.SeekTo(targetTime);
                    }
                    else
                    {
                        _engine.SeekTo(0);
                    }
                    _engine.Audio.Resume();
                    _cmdPlay = false; // Reset so dialog opens don't auto-play
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show("Error loading BMS file: " + ex.Message, "Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
            }
        }

        public void ChangeOjnDifficulty(OjnDifficulty diff)
        {
            if (_chart == null || string.IsNullOrEmpty(_chart.FilePath) || !_chart.IsOjn) return;

            try
            {
                bool wasPlaying = _engine != null && _engine.IsPlaying;
                if (_engine != null) _engine.IsPlaying = false;
                _audio.StopAll();
                _audio.StopBgm();

                _chart = OjnParser.Parse(_chart.FilePath, _audio, diff);
                _engine?.LoadChart(_chart);

                string ojnSongTitle = !string.IsNullOrEmpty(_chart.Header.Title) ? _chart.Header.Title : Path.GetFileNameWithoutExtension(_chart.FilePath);
                string diffSuffix = _chart.CurrentDifficulty.HasValue ? $" [{_chart.CurrentDifficulty}]" : "";
                Raylib.SetWindowTitle($"O2Viewer - {ojnSongTitle}{diffSuffix}");

                if (_engine != null)
                {
                    _engine.IsPlaying = wasPlaying;
                    if (wasPlaying) _audio.Resume();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[OJN] Failed to change difficulty: {ex.Message}");
            }
        }
    }

    public static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (!IpcManager.TryAcquireSingleInstance(out Mutex? mutex))
            {
                IpcManager.ForwardArgumentsToRunningInstance(args);
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                if (e.ExceptionObject is Exception unhandled)
                    Logger.Crash(unhandled);
                else
                    Logger.Error("CRASH: " + e.ExceptionObject);
            };

            try
            {
                var game = new O2Game();
                game.Run(args);
            }
            catch (Exception ex)
            {
                Logger.Crash(ex);
            }
            finally
            {
                if (mutex != null)
                {
                    try { mutex.ReleaseMutex(); } catch { }
                    mutex.Dispose();
                }
            }
        }
    }
}