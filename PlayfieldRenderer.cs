using System;
using System.Numerics;
using Raylib_cs;
using Color = Raylib_cs.Color;
using Rectangle = Raylib_cs.Rectangle;

namespace O2Play
{
    public class PlayfieldRenderer
    {
        public const int HitY = 480;
        public const int NoteHeight = 7;
        public const int KeyTopY = 480;

        // Note lane X offsets and widths (from official O2Jam Skin Note1..7 specification)
        public static readonly int[] LaneX = { 5, 33, 55, 83, 115, 143, 165 };
        public static readonly int[] LaneW = { 28, 22, 28, 32, 28, 22, 28 };

        // Key source rectangles in keydown.bmp (204x53 with 1px green chroma dividers)
        public static readonly Rectangle[] KeySrcRects = new Rectangle[7]
        {
            new Rectangle(4,   0, 27, 35),
            new Rectangle(32,  0, 26, 35),
            new Rectangle(59,  0, 27, 35),
            new Rectangle(87,  0, 30, 35),
            new Rectangle(118, 0, 27, 35),
            new Rectangle(146, 0, 26, 35),
            new Rectangle(173, 0, 27, 35)
        };

        // Key destination rectangles on playfield (from official O2Jam KeydownImage specification)
        public static readonly Rectangle[] KeyDestRects = new Rectangle[7]
        {
            new Rectangle(4,   480, 27, 35),
            new Rectangle(31,  480, 26, 35),
            new Rectangle(57,  480, 27, 35),
            new Rectangle(84,  480, 30, 35),
            new Rectangle(114, 480, 27, 35),
            new Rectangle(141, 480, 26, 35),
            new Rectangle(167, 480, 27, 35)
        };

        // Key light source rectangles in keylight.bmp (87x524, 480px active gradient starting at Y=44)
        public static readonly Rectangle[] LightSrcRects = new Rectangle[7]
        {
            new Rectangle(1,  44, 28, 480), // White/Red (Lane 1)
            new Rectangle(31, 44, 22, 480), // Blue (Lane 2)
            new Rectangle(1,  44, 28, 480), // White/Red (Lane 3)
            new Rectangle(54, 44, 32, 480), // Yellow (Lane 4)
            new Rectangle(1,  44, 28, 480), // White/Red (Lane 5)
            new Rectangle(31, 44, 22, 480), // Blue (Lane 6)
            new Rectangle(1,  44, 28, 480)  // White/Red (Lane 7)
        };

        // Key light destination rectangles (from official O2Jam Keyeffect specification: Y=0..480)
        public static readonly Rectangle[] LightDestRects = new Rectangle[7]
        {
            new Rectangle(5,   0, 28, 480),
            new Rectangle(33,  0, 22, 480),
            new Rectangle(55,  0, 28, 480),
            new Rectangle(83,  0, 32, 480),
            new Rectangle(115, 0, 28, 480),
            new Rectangle(143, 0, 22, 480),
            new Rectangle(165, 0, 28, 480)
        };

        public static readonly int[] ComboGlyphLeft = { 41, 49, 43, 43, 41, 41, 41, 44, 43, 41 };
        public static readonly int[] ComboGlyphWidth = { 45, 20, 42, 42, 46, 45, 45, 39, 41, 45 };
        public static readonly int[] ComboGlyphAdvance = { 45, 26, 43, 43, 46, 45, 45, 40, 43, 45 };

        public void Draw(GameEngine engine)
        {
            if (engine.TexPlayfield.Id > 0)
            {
                Raylib.DrawTexture(engine.TexPlayfield, 0, 0, Color.White);
            }

            // Target bar (rows 0..63 of targebar_measure.bmp at X=3, Y=416, W=192, H=64)
            if (engine.TexTargetBar.Id > 0)
            {
                Rectangle tbSrc = new Rectangle(0, 0, 191, 64);
                Rectangle tbDest = new Rectangle(3, 416, 192, 64);
                Raylib.DrawTexturePro(engine.TexTargetBar, tbSrc, tbDest, Vector2.Zero, 0f, Color.White);
            }

            double currentTick = engine.Chart.SecondsToTick(engine.CurrentTime);
            float tickScale = (float)(engine.ScrollSpeed / 192.0);

            if (engine.Chart.Measures.Count > 0)
            {
                foreach (var measure in engine.Chart.Measures)
                {
                    double deltaTicks = measure.StartTick - currentTick;
                    double mDeltaY = deltaTicks * tickScale;
                    if (mDeltaY < -50 || mDeltaY > 550) continue;

                    int mY = HitY - (int)mDeltaY;
                    if (mY >= 0 && mY <= HitY)
                    {
                        if (engine.TexTargetBar.Id > 0)
                        {
                            Rectangle mSrc = new Rectangle(0, 130, 188, 2);
                            Rectangle mDest = new Rectangle(6, mY, 188, 2);
                            Raylib.DrawTexturePro(engine.TexTargetBar, mSrc, mDest, Vector2.Zero, 0f, Color.White);
                        }
                        else
                        {
                            Raylib.DrawRectangle(6, mY, 188, 2, Color.White);
                        }
                    }
                }
            }

            bool anyLightActive = false;
            for (int i = 0; i < 7; i++)
            {
                if (engine.KeyHitFrames[i] > 0 || engine.LightFrames[i] > 0 || engine.LightTimers[i] > 0 || engine.LaneHolding[i] || engine.KeyPressed[i]) { anyLightActive = true; break; }
            }
            if (anyLightActive && engine.TexLight.Id > 0)
            {
                Raylib.BeginBlendMode(BlendMode.Additive);
                for (int i = 0; i < 7; i++)
                {
                    if (engine.KeyHitFrames[i] > 0 || engine.LightFrames[i] > 0 || engine.LightTimers[i] > 0 || engine.LaneHolding[i] || engine.KeyPressed[i])
                    {
                        Raylib.DrawTexturePro(engine.TexLight, LightSrcRects[i], LightDestRects[i], Vector2.Zero, 0f, Color.White);
                    }
                }
                Raylib.EndBlendMode();
            }

            foreach (var note in engine.Chart.Notes)
            {
                if (note.IsHit || note.IsKeysound) continue;

                double deltaTicks = note.Tick - currentTick;
                double tailDeltaTicks = (note.Tick + note.DurationTicks) - currentTick;
                double headDeltaY = deltaTicks * tickScale;
                double tailDeltaY = tailDeltaTicks * tickScale;
                if (tailDeltaY < -50 || headDeltaY > 550) continue;

                int laneIdx = note.Lane - 1;
                if (laneIdx < 0 || laneIdx > 6) continue;

                int noteX = LaneX[laneIdx];
                int noteW = LaneW[laneIdx];

                if (engine.TexNote.Id > 0)
                {
                    int totalFrames = 3;
                    int srcHeight = engine.TexNote.Height / totalFrames;
                    int frame = (int)(engine.CurrentTime * 12) % totalFrames;

                    Rectangle headSource;
                    Rectangle bodySource;
                    if (laneIdx == 3)
                    {
                        headSource = new Rectangle(28, frame * srcHeight, 32, srcHeight);
                        bodySource = new Rectangle(28, frame * srcHeight + 2, 32, 3);
                    }
                    else if (laneIdx == 1 || laneIdx == 5)
                    {
                        headSource = new Rectangle(60, frame * srcHeight, 22, srcHeight);
                        bodySource = new Rectangle(60, frame * srcHeight + 2, 22, 3);
                    }
                    else
                    {
                        headSource = new Rectangle(0, frame * srcHeight, 28, srcHeight);
                        bodySource = new Rectangle(0, frame * srcHeight + 2, 28, 3);
                    }

                    if (note.IsLongNote)
                    {
                        // Long note head bottom (held at HitY when active)
                        int headBottom = (currentTick >= note.Tick) ? HitY : HitY - (int)(deltaTicks * tickScale);
                        int headTop = headBottom - NoteHeight;

                        int tailBottom = HitY - (int)(tailDeltaTicks * tickScale);
                        int tailTop = tailBottom - NoteHeight;

                        Rectangle tailDest = new Rectangle(noteX, tailTop, noteW, NoteHeight);
                        Raylib.DrawTexturePro(engine.TexNote, headSource, tailDest, Vector2.Zero, 0f, Color.White);

                        Rectangle headDest = new Rectangle(noteX, headTop, noteW, NoteHeight);
                        Raylib.DrawTexturePro(engine.TexNote, headSource, headDest, Vector2.Zero, 0f, Color.White);

                        // Stretched body overlapping 1px at top and bottom to cover inner border seams
                        int bodyTop = tailBottom - 1;
                        int bodyBottom = headTop + 1;
                        int bodyHeight = Math.Max(0, bodyBottom - bodyTop);
                        if (bodyHeight > 0)
                        {
                            Rectangle bodyDest = new Rectangle(noteX, bodyTop, noteW, bodyHeight);
                            Raylib.DrawTexturePro(engine.TexNote, bodySource, bodyDest, Vector2.Zero, 0f, Color.White);
                        }
                    }
                    else
                    {
                        int noteBottom = HitY - (int)(deltaTicks * tickScale);
                        int noteY = noteBottom - NoteHeight;
                        Rectangle noteDest = new Rectangle(noteX, noteY, noteW, NoteHeight);
                        Raylib.DrawTexturePro(engine.TexNote, headSource, noteDest, Vector2.Zero, 0f, Color.White);
                    }
                }
                else
                {
                    int noteBottom = HitY - (int)(deltaTicks * tickScale);
                    int noteY = noteBottom - NoteHeight;
                    Raylib.DrawRectangle(noteX, noteY, noteW, NoteHeight, new Color(0, 255, 255, 255));
                }
            }

            if (engine.TexKeyDown.Id > 0)
            {
                for (int i = 0; i < 7; i++)
                {
                    if (engine.KeyHitFrames[i] > 0 || engine.KeyHitTimers[i] > 0 || engine.KeyPressed[i] || engine.LaneHolding[i])
                    {
                        Raylib.DrawTexturePro(engine.TexKeyDown, KeySrcRects[i], KeyDestRects[i], Vector2.Zero, 0f, Color.White);
                    }
                }
            }

            Raylib.BeginBlendMode(BlendMode.Additive);

            if (engine.ShowEffect)
            {
                for (int i = 0; i < 7; i++)
                {
                    if (engine.HitEffectTimers[i] > 0 && engine.TexHitEffect.Id > 0)
                    {
                        int totalFrames = 10;
                        double elapsed = 0.15 - engine.HitEffectTimers[i];
                        int frame = Math.Clamp((int)((elapsed / 0.15) * totalFrames), 0, totalFrames - 1);
                        int heW = engine.TexHitEffect.Width / totalFrames;
                        int hX = LaneX[i] + (LaneW[i] / 2) - (heW / 2);
                        int hY = (HitY - (NoteHeight / 2)) - (engine.TexHitEffect.Height / 2);
                        Raylib.DrawTexturePro(engine.TexHitEffect,
                                             new Rectangle(frame * heW, 0, heW, engine.TexHitEffect.Height),
                                             new Rectangle(hX, hY, heW, engine.TexHitEffect.Height),
                                             Vector2.Zero, 0f, Color.White);
                    }
                }

                // Long note holding effect (14 frames of 128x128 in longeffect.bmp, centered 32px below HitY)
                if (engine.TexLongEffect.Id > 0)
                {
                    foreach (var note in engine.Chart.Notes)
                    {
                        if (note.IsHolding && note.Lane >= 1 && note.Lane <= 7)
                        {
                            int lIdx = note.Lane - 1;
                            int totalFrames = 14;
                            int frame = (int)(engine.CurrentTime * 24) % totalFrames;
                            int leW = engine.TexLongEffect.Width / totalFrames;
                            int leH = engine.TexLongEffect.Height;
                            int hX = LaneX[lIdx] + (LaneW[lIdx] / 2) - (leW / 2);
                            int hY = HitY - (leH / 2) + 40;
                            Raylib.DrawTexturePro(engine.TexLongEffect,
                                                 new Rectangle(frame * leW, 0, leW, leH),
                                                 new Rectangle(hX, hY, leW, leH),
                                                 Vector2.Zero, 0f, Color.White);
                        }
                    }
                }
            }

            if (engine.DrawJudge && engine.TexJudgement.Id > 0)
            {
                float w = engine.TexJudgement.Width * engine.JudgeSize;
                float h = engine.TexJudgement.Height * engine.JudgeSize;
                Rectangle src = new Rectangle(0, 0, engine.TexJudgement.Width, engine.TexJudgement.Height);
                Rectangle dest = new Rectangle(100, 340, w, h);
                Vector2 origin = new Vector2(w / 2f, h / 2f);
                Raylib.DrawTexturePro(engine.TexJudgement, src, dest, origin, 0f, Color.White);
            }

            if (engine.DrawCombo && engine.Combo > 0 && engine.TexCombo.Id > 0)
            {
                DrawNumber(engine.TexCombo, engine.Combo, 100, (int)Math.Round(engine.ComboY));
            }

            Raylib.EndBlendMode();
        }

        public void DrawNumber(Texture2D tex, int number, int x, int y)
        {
            if (tex.Id == 0) return;
            string numStr = Math.Abs(number).ToString();
            int cellWidth = tex.Width / 10;
            float texScale = cellWidth / 128f;
            float sizeScale = numStr.Length >= 5 ? (4f / numStr.Length) : 1.0f;

            float maxAdvance = 0f;
            for (int d = 0; d < 10; d++)
            {
                if (ComboGlyphAdvance[d] > maxAdvance)
                    maxAdvance = ComboGlyphAdvance[d];
            }
            float tabWidth = maxAdvance * texScale * sizeScale;

            float srcGlyphHeight = 71f * texScale;
            float srcGlyphTop = 27f * texScale;
            float destGlyphHeight = srcGlyphHeight * sizeScale;

            float totalWidth = numStr.Length * tabWidth;
            float curX = x - (totalWidth / 2f);

            for (int i = 0; i < numStr.Length; i++)
            {
                int digit = numStr[i] - '0';
                if (digit < 0 || digit > 9) continue;

                float srcW = ComboGlyphWidth[digit] * texScale;
                float srcLeft = ComboGlyphLeft[digit] * texScale;
                float srcX = (digit * cellWidth) + srcLeft;

                float destW = srcW * sizeScale;
                float glyphOffset = (tabWidth - destW) / 2f;

                Rectangle srcRect = new Rectangle(srcX, srcGlyphTop, srcW, srcGlyphHeight);
                Rectangle destRect = new Rectangle(curX + glyphOffset, y - (destGlyphHeight / 2f), destW, destGlyphHeight);

                Raylib.DrawTexturePro(tex, srcRect, destRect, Vector2.Zero, 0f, Color.White);

                curX += tabWidth;
            }
        }
    }
}
