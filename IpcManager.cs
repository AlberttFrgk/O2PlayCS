using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace O2Play
{
    public class Win32WindowWrapper : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle { get; }
        public Win32WindowWrapper(IntPtr handle) => Handle = handle;
    }

    public static class IpcManager
    {
        public const string MutexName = "O2Play_SingleInstance_Mutex";
        public const string PipeName = "O2Play_IPC_Pipe";

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr ProcessId);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        public static extern bool AllowSetForegroundWindow(int dwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_ALWAYSONTOP = new IntPtr(-1);
        private static readonly IntPtr HWND_NOALWAYSONTOP = new IntPtr(-2);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        public const int SW_RESTORE = 9;
        public const int SW_SHOW = 5;
        public const int ASFW_ANY = -1;

        public static void SetAlwaysOnTop(IntPtr hWnd, bool alwaysOnTop)
        {
            if (hWnd == IntPtr.Zero) return;
            try
            {
                IntPtr insertAfter = alwaysOnTop ? HWND_ALWAYSONTOP : HWND_NOALWAYSONTOP;
                SetWindowPos(hWnd, insertAfter, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
            catch { }
        }

        public static void ForceForegroundWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;
            try
            {
                if (IsIconic(hWnd))
                {
                    ShowWindow(hWnd, SW_RESTORE);
                }
                else
                {
                    ShowWindow(hWnd, SW_SHOW);
                }

                IntPtr fgWnd = GetForegroundWindow();
                if (fgWnd != hWnd)
                {
                    uint fgThread = GetWindowThreadProcessId(fgWnd, IntPtr.Zero);
                    uint appThread = GetWindowThreadProcessId(hWnd, IntPtr.Zero);

                    if (fgThread != appThread)
                    {
                        AttachThreadInput(fgThread, appThread, true);
                        SetForegroundWindow(hWnd);
                        AttachThreadInput(fgThread, appThread, false);
                    }
                    else
                    {
                        SetForegroundWindow(hWnd);
                    }
                }
            }
            catch { }
        }

        public static bool TryAcquireSingleInstance(out Mutex? mutex)
        {
            try
            {
                mutex = new Mutex(true, MutexName, out bool isFirstInstance);
                return isFirstInstance;
            }
            catch (AbandonedMutexException)
            {
                mutex = new Mutex(true, MutexName, out _);
                return true;
            }
        }

        public static bool ForwardArgumentsToRunningInstance(string[] args)
        {
            try
            {
                AllowSetForegroundWindow(ASFW_ANY);
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                client.Connect(1500);
                using var writer = new StreamWriter(client, Encoding.UTF8);
                foreach (var arg in args)
                {
                    writer.WriteLine(arg);
                }
                writer.Flush();
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"[IPC Client] Failed to forward args: {ex.Message}");
                return false;
            }
        }

        public static void StartIpcServer(Action<string[]> onCommandReceived, CancellationToken token)
        {
            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await using var server = new NamedPipeServerStream(
                            PipeName,
                            PipeDirection.In,
                            NamedPipeServerStream.MaxAllowedServerInstances,
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                        await server.WaitForConnectionAsync(token);

                        using var reader = new StreamReader(server, Encoding.UTF8);
                        var list = new List<string>();
                        string? line;
                        while ((line = await reader.ReadLineAsync(token)) != null)
                        {
                            list.Add(line);
                        }

                        onCommandReceived(list.ToArray());
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (!token.IsCancellationRequested)
                        {
                            Logger.Error($"[IPC Server] {ex.Message}");
                            try { await Task.Delay(100, token); } catch { break; }
                        }
                    }
                }
            }, token);
        }
    }
}
