using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Numerics;
using Raylib_cs;
using Color = Raylib_cs.Color;

namespace O2Play
{
    public class SidebarRenderer : IDisposable
    {
        private Texture2D? _texHeader;
        private Texture2D? _texGenre;
        private Texture2D? _texArtist;
        private Texture2D? _texNotes;
        private Texture2D? _texMeasure;
        private Texture2D? _texTime;
        private Texture2D? _texBpm;
        private Texture2D? _texPlaySpeed;
        private Texture2D? _texMusicSpeed;
        private Texture2D? _texKeyCheck;
        private Texture2D? _texBgmCheck;
        private Texture2D? _texAutoCheck;
        private Texture2D? _texLowerPanel;
        private Texture2D? _texTooltip;

        private string _lastHeader = "";
        private string _lastGenre = "";
        private string _lastArtist = "";
        private string _lastNotes = "";
        private string _lastMeasure = "";
        private string _lastTime = "";
        private string _lastBpm = "";
        private string _lastPlaySpeed = "";
        private string _lastMusicSpeed = "";
        private string _lastTooltipText = "";
        private bool _lastKeyCheck = false;
        private bool _lastBgmCheck = false;
        private bool _lastEffectCheck = true;
        private OjnDifficulty? _lastLowerDifficulty = null;

        public void InvalidateLowerPanel()
        {
            UnloadTex(ref _texLowerPanel);
        }

        public void Reset()
        {
            _lastHeader = "";
            _lastGenre = "";
            _lastArtist = "";
            _lastNotes = "";
            _lastMeasure = "";
            _lastTime = "";
            _lastBpm = "";
            _lastPlaySpeed = "";
            _lastMusicSpeed = "";
            _lastTooltipText = "";
            _lastEffectCheck = true;
            _lastLowerDifficulty = null;
            UnloadTex(ref _texLowerPanel);
        }

        public void Draw(GameEngine engine)
        {
            Color panelBg = new Color(0, 96, 128, 255);
            Raylib.DrawRectangle(200, 0, 191, 600, panelBg);

            Raylib.DrawRectangle(200, 0, 1, 600, new Color(0, 48, 64, 255));
            Raylib.DrawRectangle(201, 0, 1, 600, new Color(0, 128, 168, 255));

            Raylib.DrawRectangle(202, 0, 189, 1, new Color(0, 128, 168, 255));
            Raylib.DrawRectangle(390, 0, 1, 600, new Color(0, 48, 64, 255));
            Raylib.DrawRectangle(202, 599, 189, 1, new Color(0, 48, 64, 255));

            string title = !string.IsNullOrEmpty(engine.Chart.Header.Title) ? engine.Chart.Header.Title : "Load Chart File";
            UpdateHeaderTexture(title);
            if (_texHeader.HasValue)
                Raylib.DrawTexture(_texHeader.Value, 202, 2, Color.White);

            double currentBpm = engine.Chart.Notes.Count == 0 ? 0.0 : engine.Chart.GetBpmAt(engine.CurrentTime);
            int currentMeasure = engine.TotalDuration <= 0 ? 0 : engine.Chart.SecondsToMeasure(engine.CurrentTime);
            TimeSpan timeSpan = TimeSpan.FromSeconds(engine.TotalDuration <= 0 ? 0 : engine.CurrentTime);
            string timeStr = timeSpan.ToString(@"mm\:ss");

            UpdateBoxTexture(ref _texGenre, ref _lastGenre, "   Genre: " + (engine.Chart.Header.Genre ?? ""), 187, 19);
            if (_texGenre.HasValue) Raylib.DrawTexture(_texGenre.Value, 202, 40, Color.White);

            UpdateBoxTexture(ref _texArtist, ref _lastArtist, "   Artist: " + (engine.Chart.Header.Artist ?? ""), 187, 19);
            if (_texArtist.HasValue) Raylib.DrawTexture(_texArtist.Value, 202, 72, Color.White);

            string notesStr = $"   Notes: {engine.Chart.TotalNoteCount}";
            UpdateBoxTexture(ref _texNotes, ref _lastNotes, notesStr, 187, 19);
            if (_texNotes.HasValue) Raylib.DrawTexture(_texNotes.Value, 202, 104, Color.White);

            UpdateBoxTexture(ref _texMeasure, ref _lastMeasure, "   Measure: " + currentMeasure, 187, 19);
            if (_texMeasure.HasValue) Raylib.DrawTexture(_texMeasure.Value, 202, 136, Color.White);

            UpdateBoxTexture(ref _texTime, ref _lastTime, "   Time: " + timeStr, 187, 19);
            if (_texTime.HasValue) Raylib.DrawTexture(_texTime.Value, 202, 168, Color.White);

            string bpmStr = Math.Abs(engine.MusicSpeed - 1.0f) > 0.001f
                ? $"   BPM: {currentBpm:0.0} (Scale: {currentBpm * engine.MusicSpeed:0.0})"
                : $"   BPM: {currentBpm:0.0}";
            UpdateBoxTexture(ref _texBpm, ref _lastBpm, bpmStr, 187, 19);
            if (_texBpm.HasValue) Raylib.DrawTexture(_texBpm.Value, 202, 200, Color.White);

            string playSpeedStr = engine.PlaySpeed == 0.25f ? "0.25" : engine.PlaySpeed.ToString("0.0");
            UpdateBoxTexture(ref _texPlaySpeed, ref _lastPlaySpeed, "   Play Speed: " + playSpeedStr, 187, 19);
            if (_texPlaySpeed.HasValue) Raylib.DrawTexture(_texPlaySpeed.Value, 202, 232, Color.White);

            UpdateBoxTexture(ref _texMusicSpeed, ref _lastMusicSpeed, "   Music Speed: " + engine.MusicSpeed.ToString("0.0"), 187, 19);
            if (_texMusicSpeed.HasValue) Raylib.DrawTexture(_texMusicSpeed.Value, 202, 264, Color.White);

            UpdateCheckboxTexture(ref _texKeyCheck, ref _lastKeyCheck, "   KEY", engine.IsKeySoundEnabled, 187, 19);
            if (_texKeyCheck.HasValue) Raylib.DrawTexture(_texKeyCheck.Value, 202, 296, Color.White);

            UpdateCheckboxTexture(ref _texBgmCheck, ref _lastBgmCheck, "   BGM", engine.IsBgmEnabled, 187, 19);
            if (_texBgmCheck.HasValue) Raylib.DrawTexture(_texBgmCheck.Value, 202, 328, Color.White);

            UpdateCheckboxTexture(ref _texAutoCheck, ref _lastEffectCheck, "   EFFECT", engine.ShowEffect, 187, 19);
            if (_texAutoCheck.HasValue) Raylib.DrawTexture(_texAutoCheck.Value, 202, 360, Color.White);

            DrawProgressBar(202, 392, 187, 19, engine);
            DrawLowerPanel(202, 424, 187, 171, engine);

            Vector2 mouse = Raylib.GetMousePosition();

            if (engine.Chart.IsOjn && mouse.Y >= 431 && mouse.Y <= 455)
            {
                if (mouse.X >= 208 && mouse.X <= 263 && engine.Chart.AvailableDifficulties[(int)OjnDifficulty.EX] && engine.Chart.CurrentDifficulty != OjnDifficulty.EX)
                {
                    Raylib.DrawRectangleLines(208, 431, 55, 24, new Color(0, 220, 255, 200));
                }
                else if (mouse.X >= 268 && mouse.X <= 323 && engine.Chart.AvailableDifficulties[(int)OjnDifficulty.NX] && engine.Chart.CurrentDifficulty != OjnDifficulty.NX)
                {
                    Raylib.DrawRectangleLines(268, 431, 55, 24, new Color(0, 220, 255, 200));
                }
                else if (mouse.X >= 328 && mouse.X <= 383 && engine.Chart.AvailableDifficulties[(int)OjnDifficulty.HX] && engine.Chart.CurrentDifficulty != OjnDifficulty.HX)
                {
                    Raylib.DrawRectangleLines(328, 431, 55, 24, new Color(0, 220, 255, 200));
                }
            }

            bool isOverProgressBar = mouse.X >= 200 && mouse.X <= 391 && mouse.Y >= 388 && mouse.Y <= 415;
            if (Environment.GetEnvironmentVariable("O2_TEST_HOVER") == "1")
            {
                isOverProgressBar = true;
                if (mouse.X < 202 || mouse.X > 389)
                {
                    mouse = new Vector2(295, 401);
                }
            }
            if (engine.TotalDuration > 0 && (isOverProgressBar || engine.IsDraggingProgress))
            {
                float frac = engine.IsDraggingProgress ? engine.DragFrac : Math.Clamp((mouse.X - 203f) / 184f, 0f, 1f);
                int hoverMeasure = 0;
                if (engine.Chart.Measures.Count > 0)
                {
                    hoverMeasure = Math.Clamp((int)Math.Floor(frac * engine.Chart.Measures.Count), 0, engine.Chart.Measures.Count - 1);
                }
                DrawTooltip((int)mouse.X, 376, hoverMeasure.ToString());
            }
            else if (engine.Chart.IsOjn && mouse.Y >= 431 && mouse.Y <= 455)
            {
                if (mouse.X >= 208 && mouse.X <= 263)
                {
                    int notes = engine.Chart.DifficultyNoteCounts[(int)OjnDifficulty.EX];
                    DrawTooltip((int)mouse.X, 415, engine.Chart.AvailableDifficulties[(int)OjnDifficulty.EX] ? $"Easy - {notes} notes" : "EX not available");
                }
                else if (mouse.X >= 268 && mouse.X <= 323)
                {
                    int notes = engine.Chart.DifficultyNoteCounts[(int)OjnDifficulty.NX];
                    DrawTooltip((int)mouse.X, 415, engine.Chart.AvailableDifficulties[(int)OjnDifficulty.NX] ? $"Normal - {notes} notes" : "NX not available");
                }
                else if (mouse.X >= 328 && mouse.X <= 383)
                {
                    int notes = engine.Chart.DifficultyNoteCounts[(int)OjnDifficulty.HX];
                    DrawTooltip((int)mouse.X, 415, engine.Chart.AvailableDifficulties[(int)OjnDifficulty.HX] ? $"Hard - {notes} notes" : "HX not available");
                }
            }
        }

        private void DrawProgressBar(int x, int y, int w, int h, GameEngine engine)
        {
            Raylib.DrawRectangle(x, y, w, h, new Color(64, 0, 0, 255));
            Raylib.DrawRectangle(x, y, w, 1, new Color(128, 128, 128, 255));
            Raylib.DrawRectangle(x, y + h - 1, w, 1, new Color(128, 128, 128, 255));
            Raylib.DrawRectangle(x, y, 1, h, new Color(128, 128, 128, 255));
            Raylib.DrawRectangle(x + w - 1, y, 1, h, new Color(128, 128, 128, 255));

            int maxFillW = w - 2;
            float progress = engine.IsDraggingProgress ? engine.DragFrac : engine.GetMeasureProgress();
            int fillW = (int)Math.Round(progress * maxFillW);
            if (progress >= 0.995f || (engine.TotalDuration > 0 && engine.CurrentTime >= engine.TotalDuration - 0.05))
            {
                fillW = maxFillW;
            }
            fillW = Math.Clamp(fillW, 0, maxFillW);
            if (fillW > 0)
            {
                Raylib.DrawRectangle(x + 1, y + 1, fillW, h - 2, Color.Red);
            }
        }

        private void DrawLowerPanel(int x, int y, int w, int h, GameEngine engine)
        {
            if (!_texLowerPanel.HasValue || _lastLowerDifficulty != engine.Chart.CurrentDifficulty)
            {
                _lastLowerDifficulty = engine.Chart.CurrentDifficulty;
                UnloadTex(ref _texLowerPanel);

                using (var bmp = CreateBitmap(w, h))
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.Clear(System.Drawing.Color.FromArgb(0, 80, 110));
                    using (var borderPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(0, 50, 75)))
                    {
                        g.DrawRectangle(borderPen, 0, 0, w - 1, h - 1);
                    }

                    using (var fontTitle = CreateFont("Arial", 8.5f, System.Drawing.FontStyle.Bold))
                    using (var fontText = CreateFont("Arial", 7.5f, System.Drawing.FontStyle.Regular))
                    using (var fontBtn = CreateFont("Arial", 7.5f, System.Drawing.FontStyle.Bold))
                    using (var brushTitle = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(200, 230, 255)))
                    using (var brushText = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(170, 200, 220)))
                    {
                        if (engine.Chart.IsOjn)
                        {
                            int[] btnX = { 6, 66, 126 };
                            int btnW = 55;
                            int btnH = 24;
                            string[] names = { "EX", "NX", "HX" };

                            for (int d = 0; d < 3; d++)
                            {
                                bool avail = engine.Chart.AvailableDifficulties[d];
                                bool isCurrent = (engine.Chart.CurrentDifficulty == (OjnDifficulty)d);
                                int lvl = engine.Chart.DifficultyLevels[d];
                                string label = avail ? $"{names[d]} {lvl}" : $"{names[d]} --";

                                var btnRect = new System.Drawing.Rectangle(btnX[d], 7, btnW, btnH);

                                System.Drawing.Color bgColor = isCurrent
                                    ? System.Drawing.Color.FromArgb(0, 125, 175)
                                    : (avail ? System.Drawing.Color.FromArgb(0, 48, 70) : System.Drawing.Color.FromArgb(0, 35, 50));
                                System.Drawing.Color borderColor = isCurrent
                                    ? System.Drawing.Color.FromArgb(0, 240, 255)
                                    : (avail ? System.Drawing.Color.FromArgb(0, 90, 125) : System.Drawing.Color.FromArgb(0, 50, 70));
                                System.Drawing.Color textColor = isCurrent
                                    ? System.Drawing.Color.FromArgb(255, 255, 255)
                                    : (avail ? System.Drawing.Color.FromArgb(160, 200, 220) : System.Drawing.Color.FromArgb(80, 105, 120));

                                using (var bgBrush = new System.Drawing.SolidBrush(bgColor))
                                {
                                    g.FillRectangle(bgBrush, btnRect);
                                }
                                using (var pen = new System.Drawing.Pen(borderColor, isCurrent ? 1.5f : 1.0f))
                                {
                                    g.DrawRectangle(pen, btnRect.X, btnRect.Y, btnRect.Width - 1, btnRect.Height - 1);
                                }
                                using (var textBrush = new System.Drawing.SolidBrush(textColor))
                                using (var sf = new System.Drawing.StringFormat { Alignment = System.Drawing.StringAlignment.Center, LineAlignment = System.Drawing.StringAlignment.Center })
                                {
                                    g.DrawString(label, fontBtn, textBrush, btnRect, sf);
                                }
                            }

                            using (var divPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(0, 60, 85)))
                            {
                                g.DrawLine(divPen, 6, 36, w - 7, 36);
                            }
                        }

                        var shortcutLines = new List<(string text, bool isTitle)>();
                        shortcutLines.Add(("Shortcuts:", true));
                        shortcutLines.Add(("[O]        Open BMS/OJN", false));
                        shortcutLines.Add(("[F1 / F2]  Play Speed", false));
                        shortcutLines.Add(("[1 / 2 / 3] Music Speed", false));
                        shortcutLines.Add(("[Space]    Pause / Resume", false));
                        shortcutLines.Add(("[Esc]      Exit viewer", false));

                        float areaTop = engine.Chart.IsOjn ? 37f : 0f;
                        float areaHeight = h - areaTop;
                        float lineSpacing = 18f;
                        float textBlockHeight = (shortcutLines.Count - 1) * lineSpacing + 14f;
                        float startY = areaTop + MathF.Max(0f, (areaHeight - textBlockHeight) / 2f);

                        for (int i = 0; i < shortcutLines.Count; i++)
                        {
                            var line = shortcutLines[i];
                            float curY = startY + (i * lineSpacing);
                            if (line.isTitle)
                            {
                                g.DrawString(line.text, fontTitle, brushTitle, new System.Drawing.PointF(10, curY));
                            }
                            else
                            {
                                g.DrawString(line.text, fontText, brushText, new System.Drawing.PointF(10, curY));
                            }
                        }
                    }

                    _texLowerPanel = BitmapToTexture2D(bmp);
                }
            }

            if (_texLowerPanel.HasValue)
                Raylib.DrawTexture(_texLowerPanel.Value, x, y, Color.White);
        }

        private void DrawTooltip(int mouseX, int y, string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            if (!_texTooltip.HasValue || _lastTooltipText != text)
            {
                _lastTooltipText = text;
                UnloadTex(ref _texTooltip);

                int tipH = 15;
                int tipW;
                using (var font = CreateFont("Arial", 7.5f, System.Drawing.FontStyle.Bold))
                using (var tempBmp = CreateBitmap(1, 1))
                using (var gTemp = System.Drawing.Graphics.FromImage(tempBmp))
                {
                    var size = gTemp.MeasureString(text, font);
                    tipW = Math.Max(24, (int)Math.Ceiling(size.Width) + 8);
                }

                using var bmp = CreateBitmap(tipW, tipH);
                using var g = System.Drawing.Graphics.FromImage(bmp);
                g.Clear(System.Drawing.Color.FromArgb(255, 255, 255, 180));

                using var borderPen = new System.Drawing.Pen(System.Drawing.Color.Black);
                g.DrawRectangle(borderPen, 0, 0, tipW - 1, tipH - 1);

                using var fontText = CreateFont("Arial", 7.5f, System.Drawing.FontStyle.Bold);
                using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.Black);
                using var sf = new System.Drawing.StringFormat
                {
                    Alignment = System.Drawing.StringAlignment.Center,
                    LineAlignment = System.Drawing.StringAlignment.Center
                };
                g.DrawString(text, fontText, brush, new System.Drawing.RectangleF(0, 0, tipW, tipH), sf);

                _texTooltip = BitmapToTexture2D(bmp);
            }

            if (_texTooltip.HasValue)
            {
                int tipW = _texTooltip.Value.Width;
                int tipH = _texTooltip.Value.Height;
                int tipX = mouseX - (tipW / 2);

                int minX = (tipW <= 187) ? 202 : 2;
                int maxX = Math.Max(minX, (tipW <= 187) ? (389 - tipW) : (391 - tipW - 2));
                tipX = Math.Clamp(tipX, minX, maxX);

                int tipY = y - tipH - 2;
                if (tipY < 2) tipY = y + 20;

                Raylib.DrawTexture(_texTooltip.Value, tipX, tipY, Color.White);
            }
        }

        private void UpdateHeaderTexture(string text)
        {
            if (text == _lastHeader && _texHeader.HasValue) return;
            _lastHeader = text;

            int w = 187, h = 34;
            using (var bmp = CreateBitmap(w, h))
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new System.Drawing.Rectangle(0, 0, w, h),
                    System.Drawing.Color.FromArgb(45, 90, 140),
                    System.Drawing.Color.FromArgb(20, 45, 75),
                    90f))
                {
                    g.FillRectangle(brush, 0, 0, w, h);
                }

                using (var borderPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(15, 35, 60)))
                {
                    g.DrawRectangle(borderPen, 0, 0, w - 1, h - 1);
                }

                using (var font = CreateFont("Arial", 8.5f, System.Drawing.FontStyle.Bold))
                using (var textBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(240, 245, 255)))
                using (var sf = new System.Drawing.StringFormat { Alignment = System.Drawing.StringAlignment.Center, LineAlignment = System.Drawing.StringAlignment.Center, Trimming = System.Drawing.StringTrimming.EllipsisCharacter })
                {
                    g.DrawString(text, font, textBrush, new System.Drawing.RectangleF(4, 2, w - 8, h - 4), sf);
                }

                UnloadTex(ref _texHeader);
                _texHeader = BitmapToTexture2D(bmp);
            }
        }

        private void UpdateBoxTexture(ref Texture2D? tex, ref string lastText, string text, int w, int h)
        {
            if (text == lastText && tex.HasValue) return;
            lastText = text;

            using (var bmp = CreateBitmap(w, h))
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.FromArgb(64, 0, 0));

                using (var borderPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(32, 0, 0)))
                {
                    g.DrawRectangle(borderPen, 0, 0, w - 1, h - 1);
                }

                using (var font = CreateFont("Arial", 8.5f, System.Drawing.FontStyle.Bold))
                using (var textBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(192, 192, 192)))
                {
                    g.DrawString(text, font, textBrush, new System.Drawing.PointF(0, 2));
                }

                UnloadTex(ref tex);
                tex = BitmapToTexture2D(bmp);
            }
        }

        private void UpdateCheckboxTexture(ref Texture2D? tex, ref bool lastCheck, string label, bool isChecked, int w, int h)
        {
            if (isChecked == lastCheck && tex.HasValue) return;
            lastCheck = isChecked;

            using (var bmp = CreateBitmap(w, h))
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.FromArgb(64, 0, 0));

                using (var borderPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(32, 0, 0)))
                {
                    g.DrawRectangle(borderPen, 0, 0, w - 1, h - 1);
                }

                using (var font = CreateFont("Arial", 8.5f, System.Drawing.FontStyle.Bold))
                using (var textBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(192, 192, 192)))
                {
                    g.DrawString(label, font, textBrush, new System.Drawing.PointF(0, 2));
                }

                int boxSize = 13;
                int boxX = w - 27;
                int boxY = 3;

                using (var boxBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(235, 235, 235)))
                using (var boxPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(80, 80, 80)))
                {
                    g.FillRectangle(boxBrush, boxX, boxY, boxSize, boxSize);
                    g.DrawRectangle(boxPen, boxX, boxY, boxSize - 1, boxSize - 1);
                }

                if (isChecked)
                {
                    using (var checkPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(0, 120, 0), 2f))
                    {
                        g.DrawLine(checkPen, boxX + 2, boxY + 6, boxX + 5, boxY + 10);
                        g.DrawLine(checkPen, boxX + 5, boxY + 10, boxX + 10, boxY + 3);
                    }
                }

                UnloadTex(ref tex);
                tex = BitmapToTexture2D(bmp);
            }
        }

        // Locks GDI+ rendering to 120 DPI (125% Windows scaling) across all displays
        private const float TargetDpi = 120f;

        private static System.Drawing.Bitmap CreateBitmap(int w, int h)
        {
            var bmp = new System.Drawing.Bitmap(w, h);
            bmp.SetResolution(TargetDpi, TargetDpi);
            return bmp;
        }

        private static System.Drawing.Font CreateFont(string familyName, float pointSize, System.Drawing.FontStyle style)
        {
            float pixelSize = pointSize * (TargetDpi / 72f);
            return new System.Drawing.Font(familyName, pixelSize, style, System.Drawing.GraphicsUnit.Pixel);
        }

        public unsafe Texture2D BitmapToTexture2D(System.Drawing.Bitmap bmp)
        {
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            byte[] bytes = ms.ToArray();
            Raylib_cs.Image img;
            fixed (byte* pExt = ".png\0"u8)
            fixed (byte* p = bytes)
            {
                img = Raylib.LoadImageFromMemory((sbyte*)pExt, p, bytes.Length);
            }
            var tex = Raylib.LoadTextureFromImage(img);
            Raylib.UnloadImage(img);
            Raylib.SetTextureFilter(tex, TextureFilter.Point);
            return tex;
        }

        private void UnloadTex(ref Texture2D? tex)
        {
            if (tex.HasValue && tex.Value.Id > 0)
            {
                Raylib.UnloadTexture(tex.Value);
            }
            tex = null;
        }

        public void Dispose()
        {
            UnloadTex(ref _texHeader);
            UnloadTex(ref _texGenre);
            UnloadTex(ref _texArtist);
            UnloadTex(ref _texNotes);
            UnloadTex(ref _texMeasure);
            UnloadTex(ref _texTime);
            UnloadTex(ref _texBpm);
            UnloadTex(ref _texPlaySpeed);
            UnloadTex(ref _texMusicSpeed);
            UnloadTex(ref _texKeyCheck);
            UnloadTex(ref _texBgmCheck);
            UnloadTex(ref _texAutoCheck);
            UnloadTex(ref _texTooltip);
            UnloadTex(ref _texLowerPanel);
        }
    }
}
