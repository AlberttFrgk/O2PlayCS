using System;
using System.IO;

namespace O2Play
{
    public static class Logger
    {
        private static readonly object _lock = new();
        private static readonly string CrashLogPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");

        // Info and warning logs are intentionally disabled so log files do not accumulate and grow huge over time.
        public static void Info(string message) { }
        public static void Warn(string message) { }

        // Only crashes are recorded to crash.log
        public static void Error(string message)
        {
            if (message != null && message.StartsWith("CRASH", StringComparison.OrdinalIgnoreCase))
            {
                WriteCrash(message);
            }
        }

        public static void Crash(Exception ex)
        {
            WriteCrash(ex.ToString());
        }

        public static void WriteCrash(string details)
        {
            try
            {
                lock (_lock)
                {
                    File.AppendAllText(
                        CrashLogPath,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [CRASH] {details}{Environment.NewLine}");
                }
            }
            catch
            {
                // Logging must never crash the app.
            }
        }
    }
}
