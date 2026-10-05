using System;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace O2Play
{
    public class UserSettings
    {
        public bool AlwaysOnTop { get; set; } = false;
        public float PlaySpeed { get; set; } = 2.0f;
        public bool ShowEffect { get; set; } = true;
        public int FPSTarget { get; set; } = 500;

        private static readonly string SettingsIniPath = Path.Combine(AppContext.BaseDirectory, "settings.ini");
        private static readonly string SettingsJsonPath = Path.Combine(AppContext.BaseDirectory, "settings.json");
        private static UserSettings? _cached;

        public static UserSettings Load()
        {
            if (_cached != null) return _cached;

            var settings = new UserSettings();

            try
            {
                if (File.Exists(SettingsIniPath))
                {
                    ParseIni(File.ReadAllLines(SettingsIniPath), settings);
                    _cached = settings;
                    return _cached;
                }

                // Migrate from legacy settings.json if present
                if (File.Exists(SettingsJsonPath))
                {
                    try
                    {
                        string json = File.ReadAllText(SettingsJsonPath);
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("PlaySpeed", out var speedProp) && speedProp.TryGetSingle(out float spd))
                            settings.PlaySpeed = (float)Math.Round(Math.Clamp(spd, 0.25f, 8.0f), 2);
                        if (root.TryGetProperty("ShowEffect", out var effectProp))
                            settings.ShowEffect = effectProp.GetBoolean();
                        if (root.TryGetProperty("AlwaysOnTop", out var alwaysProp))
                            settings.AlwaysOnTop = alwaysProp.GetBoolean();
                        if (root.TryGetProperty("FPSTarget", out var fpsProp) && fpsProp.TryGetInt32(out int fps) && fps > 0)
                            settings.FPSTarget = fps;
                    }
                    catch
                    {
                    }

                    Save(settings);
                    try { File.Delete(SettingsJsonPath); } catch { }

                    _cached = settings;
                    return _cached;
                }
            }
            catch
            {
            }

            _cached = settings;
            Save(_cached);
            return _cached;
        }

        private static void ParseIni(string[] lines, UserSettings settings)
        {
            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith(";") || line.StartsWith("#") || line.StartsWith("["))
                    continue;

                int eqIdx = line.IndexOf('=');
                if (eqIdx <= 0) continue;

                string key = line.Substring(0, eqIdx).Trim();
                string val = line.Substring(eqIdx + 1).Trim();

                if (key.Equals("AlwaysOnTop", StringComparison.OrdinalIgnoreCase))
                {
                    settings.AlwaysOnTop = ParseBool(val, false);
                }
                else if (key.Equals("PlaySpeed", StringComparison.OrdinalIgnoreCase))
                {
                    if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float spd))
                    {
                        settings.PlaySpeed = (float)Math.Round(Math.Clamp(spd, 0.25f, 8.0f), 2);
                    }
                }
                else if (key.Equals("ShowEffect", StringComparison.OrdinalIgnoreCase))
                {
                    settings.ShowEffect = ParseBool(val, true);
                }
                else if (key.Equals("FPSTarget", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(val, out int fps) && fps > 0)
                    {
                        settings.FPSTarget = fps;
                    }
                }
            }
        }

        private static bool ParseBool(string val, bool defaultValue)
        {
            if (val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                val.Equals("yes", StringComparison.OrdinalIgnoreCase) || val.Equals("on", StringComparison.OrdinalIgnoreCase))
                return true;
            if (val == "0" || val.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                val.Equals("no", StringComparison.OrdinalIgnoreCase) || val.Equals("off", StringComparison.OrdinalIgnoreCase))
                return false;
            return defaultValue;
        }

        public static void SavePlaySpeed(float playSpeed)
        {
            var settings = Load();
            settings.PlaySpeed = (float)Math.Round(Math.Clamp(playSpeed, 0.25f, 8.0f), 2);
            Save(settings);
        }

        public static void SaveShowEffect(bool showEffect)
        {
            var settings = Load();
            settings.ShowEffect = showEffect;
            Save(settings);
        }

        public static void SaveAlwaysOnTop(bool alwaysOnTop)
        {
            var settings = Load();
            settings.AlwaysOnTop = alwaysOnTop;
            Save(settings);
        }

        public static void SaveFPSTarget(int fpsTarget)
        {
            var settings = Load();
            settings.FPSTarget = Math.Max(30, fpsTarget);
            Save(settings);
        }

        private static void Save(UserSettings settings)
        {
            try
            {
                using var sw = new StreamWriter(SettingsIniPath, false, System.Text.Encoding.UTF8);
                sw.WriteLine("[Settings]");
                sw.WriteLine($"AlwaysOnTop={(settings.AlwaysOnTop ? 1 : 0)}");
                sw.WriteLine($"FPSTarget={settings.FPSTarget}");
                sw.WriteLine($"PlaySpeed={(settings.PlaySpeed == 0.25f ? "0.25" : settings.PlaySpeed.ToString("0.0", CultureInfo.InvariantCulture))}");
                sw.WriteLine($"ShowEffect={(settings.ShowEffect ? 1 : 0)}");
            }
            catch
            {
            }
        }
    }
}
