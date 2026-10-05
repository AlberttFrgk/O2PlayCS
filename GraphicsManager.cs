using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Raylib_cs;
using Rectangle = Raylib_cs.Rectangle;

namespace O2Play
{
    public class GraphicsManager : IDisposable
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [DllImport("opengl32.dll", EntryPoint = "glGetString")]
        private static extern IntPtr GlGetString(uint name);

        private const uint GL_VENDOR = 0x1F00;
        private const uint GL_RENDERER = 0x1F01;
        private const uint GL_VERSION = 0x1F02;

        public static void InitializeGpuPreference()
        {
            try
            {
                bool hasNvidia = false;
                bool hasAmd = false;
                bool hasIntel = false;
                bool hasDiscrete = false;

                for (uint i = 0; ; i++)
                {
                    var d = new DISPLAY_DEVICE();
                    d.cb = Marshal.SizeOf(d);
                    if (!EnumDisplayDevices(null, i, ref d, 0)) break;
                    if (string.IsNullOrWhiteSpace(d.DeviceString)) continue;

                    string name = d.DeviceString.Trim();

                    bool isNvidia = name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                                    name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                                    name.Contains("Quadro", StringComparison.OrdinalIgnoreCase);

                    bool isAmd = name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                                 name.Contains("Radeon", StringComparison.OrdinalIgnoreCase);

                    bool isIntel = name.Contains("Intel", StringComparison.OrdinalIgnoreCase);

                    bool isDgpu = isNvidia ||
                                  (isAmd && (name.Contains("RX", StringComparison.OrdinalIgnoreCase) ||
                                             name.Contains("Pro", StringComparison.OrdinalIgnoreCase) ||
                                             name.Contains("XT", StringComparison.OrdinalIgnoreCase) ||
                                             !name.Contains("Graphics", StringComparison.OrdinalIgnoreCase))) ||
                                  (isIntel && name.Contains("Arc", StringComparison.OrdinalIgnoreCase));

                    if (isNvidia) hasNvidia = true;
                    if (isAmd) hasAmd = true;
                    if (isIntel) hasIntel = true;
                    if (isDgpu) hasDiscrete = true;

                    string gpuType = isDgpu ? "Discrete GPU" : "Integrated GPU";
                    Logger.Info($"[GPU] Adapter #{i}: {name} ({gpuType})");
                }

                // Generic Windows compatibility shim for modern GPU scheduling
                Environment.SetEnvironmentVariable("SHIM_MCCOMPAT", "0x000000001");

                if (hasNvidia)
                {
                    Environment.SetEnvironmentVariable("__NV_PRIME_RENDER_OFFLOAD", "1");
                    Environment.SetEnvironmentVariable("__GLX_VENDOR_LIBRARY_NAME", "nvidia");
                }
                else
                {
                    // Clean up any stale NVIDIA-specific variables when on AMD or Intel
                    Environment.SetEnvironmentVariable("__GLX_VENDOR_LIBRARY_NAME", null);
                    Environment.SetEnvironmentVariable("__NV_PRIME_RENDER_OFFLOAD", null);
                }

                if (hasAmd)
                {
                    Environment.SetEnvironmentVariable("DRI_PRIME", "1");
                    Environment.SetEnvironmentVariable("GPU_MAX_HEAP_SIZE", "100");
                    Environment.SetEnvironmentVariable("GPU_MAX_ALLOC_PERCENT", "100");
                }

                // If only Intel iGPU is present, ensure clean hardware dispatch
                if (hasIntel && !hasDiscrete)
                {
                    Environment.SetEnvironmentVariable("DRI_PRIME", "0");
                }

                // Configure Windows DirectX UserGpuPreferences
                // GpuPreference=2: High Performance (Discrete GPU: NVIDIA / AMD / Intel Arc)
                // GpuPreference=1: Minimum Power / Hardware Integrated GPU (Intel UHD / Iris Xe / AMD APU)
                string targetPreference = hasDiscrete ? "GpuPreference=2;" : "GpuPreference=1;";

                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences");
                    if (key != null)
                    {
                        object? val = key.GetValue(exePath);
                        string valStr = val?.ToString() ?? "";
                        if (!valStr.Contains(targetPreference))
                        {
                            key.SetValue(exePath, targetPreference, RegistryValueKind.String);
                            string modeStr = hasDiscrete ? "High-Performance dGPU" : "Hardware iGPU";
                            Logger.Info($"[GPU] Configured Windows Graphics preference to {modeStr} ({targetPreference}) for {Path.GetFileName(exePath)}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[GPU] Preference initialization note: {ex.Message}");
            }
        }

        public static void LogActiveGpu()
        {
            try
            {
                IntPtr pRenderer = GlGetString(GL_RENDERER);
                IntPtr pVendor = GlGetString(GL_VENDOR);
                IntPtr pVersion = GlGetString(GL_VERSION);

                string renderer = pRenderer != IntPtr.Zero ? Marshal.PtrToStringAnsi(pRenderer) ?? "Unknown" : "Unknown";
                string vendor = pVendor != IntPtr.Zero ? Marshal.PtrToStringAnsi(pVendor) ?? "Unknown" : "Unknown";
                string version = pVersion != IntPtr.Zero ? Marshal.PtrToStringAnsi(pVersion) ?? "Unknown" : "Unknown";

                bool isSoftware = renderer.Contains("GDI Generic", StringComparison.OrdinalIgnoreCase) ||
                                  renderer.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ||
                                  renderer.Contains("llvmpipe", StringComparison.OrdinalIgnoreCase) ||
                                  renderer.Contains("Software", StringComparison.OrdinalIgnoreCase);

                string status = isSoftware ? "[WARNING: Running in CPU Software Rendering!]" : "[Hardware Accelerated]";

                string info = $"[GPU] Active OpenGL Context: {renderer} | Vendor: {vendor} | Version: {version} {status}";
                Logger.Info(info);
                Console.WriteLine(info);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[GPU] Could not query active OpenGL context: {ex.Message}");
            }
        }
        private readonly GameEngine? _engine;

        public PlayfieldRenderer Playfield { get; } = new();
        public SidebarRenderer Sidebar { get; } = new();

        public GraphicsManager() { }

        public GraphicsManager(GameEngine engine)
        {
            _engine = engine;
        }

        public const int HitY = PlayfieldRenderer.HitY;
        public const int NoteHeight = PlayfieldRenderer.NoteHeight;
        public const int KeyTopY = PlayfieldRenderer.KeyTopY;

        public static readonly int[] LaneX = PlayfieldRenderer.LaneX;
        public static readonly int[] LaneW = PlayfieldRenderer.LaneW;

        public static readonly Rectangle[] KeySrcRects = PlayfieldRenderer.KeySrcRects;
        public static readonly Rectangle[] KeyDestRects = PlayfieldRenderer.KeyDestRects;
        public static readonly Rectangle[] LightSrcRects = PlayfieldRenderer.LightSrcRects;
        public static readonly Rectangle[] LightDestRects = PlayfieldRenderer.LightDestRects;

        public static readonly int[] ComboGlyphLeft = PlayfieldRenderer.ComboGlyphLeft;
        public static readonly int[] ComboGlyphWidth = PlayfieldRenderer.ComboGlyphWidth;
        public static readonly int[] ComboGlyphAdvance = PlayfieldRenderer.ComboGlyphAdvance;

        public void InvalidateLowerPanel() => Sidebar.InvalidateLowerPanel();

        public void Reset() => Sidebar.Reset();

        public void Draw()
        {
            if (_engine != null)
            {
                Draw(_engine);
            }
        }

        public void Draw(GameEngine engine)
        {
            Playfield.Draw(engine);
            Sidebar.Draw(engine);
        }

        public void DrawNumber(Texture2D tex, int number, int x, int y)
        {
            Playfield.DrawNumber(tex, number, x, y);
        }

        public void Dispose()
        {
            Sidebar.Dispose();
        }
    }

    public class GameInterface : GraphicsManager
    {
        public GraphicsManager Graphics => this;

        public GameInterface(GameEngine engine) : base(engine) { }
    }
}
