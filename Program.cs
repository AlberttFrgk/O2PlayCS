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

        // Exported flags for NVIDIA Optimus and AMD PowerXpress to request discrete GPU
        [UnmanagedCallersOnly(EntryPoint = "NvOptimusEnablement")]
        public static uint NvOptimusEnablement() => 0x00000001;

        [UnmanagedCallersOnly(EntryPoint = "AmdPowerXpressRequestHighPerformance")]
        public static int AmdPowerXpressRequestHighPerformance() => 1;

        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static extern uint TimeBeginPeriod(uint uMilliseconds);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        private static extern uint TimeEndPeriod(uint uMilliseconds);

        private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData);

        [DllImport("comctl32.dll", ExactSpelling = true)]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, UIntPtr dwRefData);

        [DllImport("comctl32.dll", ExactSpelling = true)]
        private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass);

        [DllImport("comctl32.dll", ExactSpelling = true)]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern IntPtr SetTimer(IntPtr hWnd, IntPtr nIDEvent, uint uElapse, IntPtr lpTimerFunc);

        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern bool KillTimer(IntPtr hWnd, IntPtr nIDEvent);

        private const uint WM_TIMER = 0x0113;
        private const uint WM_ENTERSIZEMOVE = 0x0231;
        private const uint WM_EXITSIZEMOVE = 0x0232;
        private const int DragTimerId = 9999;

        private SubclassProc? _subclassProc;
        private bool _isModalDragging = false;
        private long _lastFrameTick = 0;

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
                    if (parsed.Measure > 0 && _chart.Notes.Count > 0)
                    {
                        double targetTime = _chart.TickToSeconds(parsed.Measure * 192.0);
                        _engine.SeekTo(targetTime);
                    }
                    _engine.Resume();
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

            GraphicsManager.InitializeGpuPreference();

            TimeBeginPeriod(1);
            try
            {
                Raylib.InitWindow(391, 600, "O2Viewer");
                Raylib.SetExitKey(KeyboardKey.Null); // GameEngine handles Escape when window is focused
                Raylib.SetTargetFPS(0);

                if (UserSettings.Load().AlwaysOnTop)
                {
                    Raylib.SetWindowState(ConfigFlags.TopmostWindow);
                    IpcManager.SetAlwaysOnTop(GetHwnd(), true);
                }

                GraphicsManager.LogActiveGpu();

                _audio = new AudioEngine();
                _chart = ChartManager.CreateEmptyChart();

                string initTitle = !string.IsNullOrEmpty(_chart.Header.Title) ? $"O2Viewer - {_chart.Header.Title}" : "O2Viewer";
                Raylib.SetWindowTitle(initTitle);

                _engine = new GameEngine(_chart, _audio);
                _engine.RequestOpenFile += OpenBmsDialog;
                _engine.RequestChangeDifficulty += ChangeOjnDifficulty;

                IntPtr hwnd = GetHwnd();
                _subclassProc = WindowSubclass;
                SetWindowSubclass(hwnd, _subclassProc, (UIntPtr)1, UIntPtr.Zero);

                if (initialArgs.Length > 0)
                {
                    ProcessCommandLineArgs(initialArgs, isInitial: true);
                    if (_exitRequested)
                    {
                        if (hwnd != IntPtr.Zero && _subclassProc != null)
                        {
                            KillTimer(hwnd, (IntPtr)DragTimerId);
                            RemoveWindowSubclass(hwnd, _subclassProc, (UIntPtr)1);
                        }
                        _audio.StopAll();
                        _engine?.Dispose();
                        Raylib.CloseWindow();
                        return;
                    }
                }

                int _testFrameCounter = 0;
                _lastFrameTick = System.Diagnostics.Stopwatch.GetTimestamp();
                double targetFps = UserSettings.Load().FPSTarget > 0 ? UserSettings.Load().FPSTarget : 500.0;

                while (!Raylib.WindowShouldClose())
                {
                    while (_pendingIpcCommands.TryDequeue(out var ipcArgs))
                    {
                        ProcessCommandLineArgs(ipcArgs, isInitial: false);
                    }

                    if (Raylib.IsWindowFocused() && Raylib.IsKeyPressed(KeyboardKey.Escape))
                    {
                        break;
                    }

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

                    if (_pendingFileToLoad != null)
                    {
                        string file = _pendingFileToLoad;
                        OjnDifficulty? diff = _pendingDifficultyToLoad >= 0 ? (OjnDifficulty)_pendingDifficultyToLoad : null;
                        _pendingFileToLoad = null;
                        _pendingDifficultyToLoad = -1;
                        LoadBmsFile(file, diff);
                    }

                    bool isWindowActive = (Raylib.IsWindowFocused() || _isModalDragging) && !Raylib.IsWindowMinimized();

                    if (!isWindowActive)
                    {
                        if (_engine.IsPlaying && !_pausedDueToBackground)
                        {
                            _pausedDueToBackground = true;
                            _engine.Pause();
                        }
                    }
                    else
                    {
                        if (_pausedDueToBackground)
                        {
                            _pausedDueToBackground = false;
                            if (_engine.CanResume())
                            {
                                _engine.Resume();
                            }
                        }
                    }

                    // Throttle frame rate when unfocused or minimized to keep CPU cool
                    double effectiveTargetFps = isWindowActive ? targetFps : Math.Min(60.0, targetFps);
                    float dt = (float)PreciseFrameLimit(ref _lastFrameTick, effectiveTargetFps);
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

                if (hwnd != IntPtr.Zero && _subclassProc != null)
                {
                    KillTimer(hwnd, (IntPtr)DragTimerId);
                    RemoveWindowSubclass(hwnd, _subclassProc, (UIntPtr)1);
                }

                _ipcCancelSource?.Cancel();
                _audio.StopAll();
                _engine?.Dispose();
                Raylib.CloseWindow();
            }
            finally
            {
                TimeEndPeriod(1);
            }
        }

        private IntPtr WindowSubclass(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData)
        {
            switch (uMsg)
            {
                case WM_ENTERSIZEMOVE:
                    _isModalDragging = true;
                    _lastFrameTick = System.Diagnostics.Stopwatch.GetTimestamp();
                    SetTimer(hWnd, (IntPtr)DragTimerId, 15, IntPtr.Zero);
                    break;

                case WM_EXITSIZEMOVE:
                    KillTimer(hWnd, (IntPtr)DragTimerId);
                    _isModalDragging = false;
                    _lastFrameTick = System.Diagnostics.Stopwatch.GetTimestamp();
                    break;

                case WM_TIMER:
                    if (wParam == (IntPtr)DragTimerId)
                    {
                        if (!Raylib.WindowShouldClose() && _engine != null && _audio != null)
                        {
                            long curTick = System.Diagnostics.Stopwatch.GetTimestamp();
                            double delta = (curTick - _lastFrameTick) / (double)System.Diagnostics.Stopwatch.Frequency;
                            _lastFrameTick = curTick;
                            if (delta > 0.1) delta = 0.1;
                            if (delta <= 0.0) delta = 0.0001;

                            _audio.Update();
                            _engine.IsWindowFocused = true;
                            _engine.Update((float)delta);

                            Raylib.BeginDrawing();
                            Raylib.ClearBackground(new Color(0, 96, 128, 255));
                            _engine.Draw();
                            Raylib.EndDrawing();
                        }
                        return IntPtr.Zero;
                    }
                    break;
            }

            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        private static unsafe IntPtr GetHwnd() => (IntPtr)Raylib.GetWindowHandle();

        private static double PreciseFrameLimit(ref long prevTick, double maxFrameRate)
        {
            long freq = System.Diagnostics.Stopwatch.Frequency;
            long curTick = System.Diagnostics.Stopwatch.GetTimestamp();

            if (maxFrameRate > 0.0)
            {
                long targetTicks = (long)(freq / maxFrameRate);
                long elapsed = curTick - prevTick;

                if (elapsed < targetTicks)
                {
                    double remainingMs = (targetTicks - elapsed) * 1000.0 / freq;
                    // With TimeBeginPeriod(1), Thread.Sleep achieves 1ms precision without spinning
                    if (remainingMs >= 1.5)
                    {
                        Thread.Sleep((int)(remainingMs - 1.0));
                    }

                    // For the remaining sub-millisecond tail, pause CPU to reduce power/heat
                    while (true)
                    {
                        curTick = System.Diagnostics.Stopwatch.GetTimestamp();
                        if (curTick - prevTick >= targetTicks)
                        {
                            break;
                        }
                        Thread.SpinWait(10);
                    }
                }
            }

            double delta = (curTick - prevTick) / (double)freq;
            prevTick = curTick;

            if (delta > 0.1) delta = 0.1;
            if (delta <= 0.0) delta = 0.0001;

            return delta;
        }

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