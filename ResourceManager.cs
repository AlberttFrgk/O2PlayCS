using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Raylib_cs;
using Color = Raylib_cs.Color;

namespace O2Play
{
    public class ResourceManager : IDisposable
    {
        public static readonly Dictionary<string, string> Resource = new(StringComparer.OrdinalIgnoreCase)
        {
            ["app_icon.ico"] = "app_icon.ico",
            ["combo_number.bmp"] = "combo_number.bmp",
            ["hiteffect.bmp"] = "hiteffect.bmp",
            ["judgement.bmp"] = "judgement.bmp",
            ["playfield.bmp"] = "playfield.bmp",
            ["keydown.bmp"] = "keydown.bmp",
            ["keylight.bmp"] = "keylight.bmp",
            ["longeffect.bmp"] = "longeffect.bmp",
            ["note.bmp"] = "note.bmp",
            ["targebar_measure.bmp"] = "targebar_measure.bmp"
        };

        private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.OrdinalIgnoreCase);

        public Texture2D TexPlayfield { get; private set; }
        public Texture2D TexKeyDown { get; private set; }
        public Texture2D TexNote { get; private set; }
        public Texture2D TexCombo { get; private set; }
        public Texture2D TexHitEffect { get; private set; }
        public Texture2D TexLongEffect { get; private set; }
        public Texture2D TexJudgement { get; private set; }
        public Texture2D TexLight { get; private set; }
        public Texture2D TexTargetBar { get; private set; }

        public void LoadGameAssets()
        {
            TexPlayfield = LoadTexture("playfield.bmp", false);
            TexKeyDown = LoadTexture("keydown.bmp", true);
            TexLight = LoadTexture("keylight.bmp", true);
            TexNote = LoadTexture("note.bmp", true);
            TexCombo = LoadTexture("combo_number.bmp", true);
            Raylib.SetTextureFilter(TexCombo, TextureFilter.Bilinear);
            TexHitEffect = LoadTexture("hiteffect.bmp", true);
            TexLongEffect = LoadTexture("longeffect.bmp", true);
            TexJudgement = LoadTexture("judgement.bmp", true);
            Raylib.SetTextureFilter(TexJudgement, TextureFilter.Bilinear);
            TexTargetBar = LoadTexture("targebar_measure.bmp", false);
        }

        public unsafe Texture2D LoadTexture(string resourceName, bool useColorKey = true)
        {
            string fileName = Path.GetFileName(resourceName.Replace("O2Play.Assets.", ""));
            string targetFile = Resource.TryGetValue(fileName, out var mapped)
                ? mapped
                : (Resource.TryGetValue(fileName + ".bmp", out var mappedWithExt) ? mappedWithExt : fileName);

            string cacheKey = $"{targetFile}_{useColorKey}";
            if (_textures.TryGetValue(cacheKey, out var cachedTex))
            {
                return cachedTex;
            }

            var assembly = Assembly.GetExecutingAssembly();
            Stream? stream = assembly.GetManifestResourceStream("O2Play.Assets." + targetFile);

            if (stream == null)
            {
                string[] assetDirs =
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets"),
                    Path.Combine(Directory.GetCurrentDirectory(), "Assets")
                };

                foreach (var dir in assetDirs)
                {
                    string diskPath = Path.Combine(dir, targetFile);
                    if (File.Exists(diskPath))
                    {
                        try
                        {
                            stream = File.OpenRead(diskPath);
                            break;
                        }
                        catch { }
                    }
                }
            }

            if (stream == null)
            {
                Logger.Warn($"[Assets] Could not find asset: {resourceName}");
                var fallbackImage = Raylib.GenImageColor(1, 1, Color.White);
                var fallbackTexture = Raylib.LoadTextureFromImage(fallbackImage);
                Raylib.UnloadImage(fallbackImage);
                _textures[cacheKey] = fallbackTexture;
                return fallbackTexture;
            }

            using (stream)
            using (var memoryStream = new MemoryStream())
            {
                stream.CopyTo(memoryStream);
                byte[] fileBytes = memoryStream.ToArray();

                string extension = Path.GetExtension(targetFile);
                if (string.IsNullOrEmpty(extension)) extension = ".bmp";
                byte[] extensionBytes = Encoding.ASCII.GetBytes(extension + "\0");

                Raylib_cs.Image image;
                fixed (byte* pExtension = extensionBytes)
                fixed (byte* pBytes = fileBytes)
                {
                    image = Raylib.LoadImageFromMemory((sbyte*)pExtension, pBytes, fileBytes.Length);
                }

                if (image.Data == null || image.Width <= 0 || image.Height <= 0)
                {
                    Logger.Warn($"[Assets] Failed to decode image from memory: {resourceName}");
                    var fallbackImage = Raylib.GenImageColor(1, 1, Color.White);
                    var fallbackTexture = Raylib.LoadTextureFromImage(fallbackImage);
                    Raylib.UnloadImage(fallbackImage);
                    _textures[cacheKey] = fallbackTexture;
                    return fallbackTexture;
                }

                if (useColorKey)
                {
                    Raylib.ImageFormat(ref image, PixelFormat.UncompressedR8G8B8A8);
                    Color* pixels = (Color*)image.Data;
                    if (pixels != null)
                    {
                        int pixelCount = image.Width * image.Height;

                        for (int i = 0; i < pixelCount; i++)
                        {
                            Color pixel = pixels[i];

                            // O2Jam sprite sheets use pure black, chroma-green, or magenta for transparency.
                            if (pixel.R == 0 && pixel.G == 0 && pixel.B == 0)
                            {
                                pixels[i] = Color.Blank;
                            }
                            else if (pixel.G > 200 && pixel.R < 50 && pixel.B < 50)
                            {
                                pixels[i] = Color.Blank;
                            }
                            else if (pixel.R == 255 && pixel.G == 0 && pixel.B == 255)
                            {
                                pixels[i] = Color.Blank;
                            }
                        }
                    }
                }

                var texture = Raylib.LoadTextureFromImage(image);
                Raylib.UnloadImage(image);
                Raylib.SetTextureFilter(texture, TextureFilter.Point);
                _textures[cacheKey] = texture;
                return texture;
            }
        }

        public void Dispose()
        {
            foreach (var kvp in _textures)
            {
                if (kvp.Value.Id > 0)
                {
                    Raylib.UnloadTexture(kvp.Value);
                }
            }
            _textures.Clear();
        }
    }
}
