using System;
using System.IO;
using System.Text.Json;

namespace O2Play
{
    public class UserSettings
    {
        public float PlaySpeed { get; set; } = 2.0f;
        public bool ShowEffect { get; set; } = true;

        private static readonly string SettingsFilePath = Path.Combine(AppContext.BaseDirectory, "settings.json");
        private static UserSettings? _cached;

        public static UserSettings Load()
        {
            if (_cached != null) return _cached;

            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var loaded = JsonSerializer.Deserialize<UserSettings>(json);
                    if (loaded != null)
                    {
                        if (loaded.PlaySpeed < 0.5f || loaded.PlaySpeed > 8.0f)
                            loaded.PlaySpeed = 2.0f;

                        _cached = loaded;
                        return _cached;
                    }
                }
            }
            catch
            {
                // Fallback to default
            }

            _cached = new UserSettings();
            return _cached;
        }

        public static void SavePlaySpeed(float playSpeed)
        {
            var settings = Load();
            settings.PlaySpeed = (float)Math.Round(Math.Clamp(playSpeed, 0.5f, 8.0f), 1);
            Save(settings);
        }

        public static void SaveShowEffect(bool showEffect)
        {
            var settings = Load();
            settings.ShowEffect = showEffect;
            Save(settings);
        }

        private static void Save(UserSettings settings)
        {
            try
            {
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch
            {
                // Silently ignore if cannot write
            }
        }
    }
}
