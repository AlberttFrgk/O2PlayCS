using System;
using System.Numerics;
using Raylib_cs;

namespace O2Play
{
    public class InputManager
    {
        public static readonly KeyboardKey[] PlayKeys =
        {
            KeyboardKey.S, KeyboardKey.D, KeyboardKey.F, KeyboardKey.Space,
            KeyboardKey.J, KeyboardKey.K, KeyboardKey.L
        };

        private bool _isDraggingProgress = false;
        private bool _wasPlayingBeforeDrag = false;
        private int _dragStartX = 0;
        private float _dragFrac = 0f;
        private float _lastSeekFrac = -1f;

        public bool IsDraggingProgress
        {
            get => _isDraggingProgress;
            set => _isDraggingProgress = value;
        }

        public float DragFrac
        {
            get => _dragFrac;
            set => _dragFrac = value;
        }

        public float LastSeekFrac
        {
            get => _lastSeekFrac;
            set => _lastSeekFrac = value;
        }

        public event Action? RequestOpenFile;

        public void Update(GameEngine engine, JudgeManager judge)
        {
            if (!engine.IsWindowFocused)
            {
                Array.Clear(engine.Animation.KeyPressed, 0, 7);
                if (_isDraggingProgress)
                {
                    _isDraggingProgress = false;
                    engine.IsPlaying = _wasPlayingBeforeDrag;
                    engine.SeekToFrac(_dragFrac);
                    if (_wasPlayingBeforeDrag) engine.AudioMgr.Resume();
                }
                return;
            }

            // Autoplay preview mode: ignore keyboard input for play keys
            Array.Clear(engine.Animation.KeyPressed, 0, 7);

            if (Raylib.IsKeyPressed(KeyboardKey.F1))
            {
                engine.DecreasePlaySpeed();
            }
            if (Raylib.IsKeyPressed(KeyboardKey.F2))
            {
                engine.IncreasePlaySpeed();
            }

            if (Raylib.IsKeyPressed(KeyboardKey.One) || Raylib.IsKeyPressed(KeyboardKey.Kp1))
            {
                engine.MusicSpeed = (float)Math.Round(Math.Max(0.1f, engine.MusicSpeed - 0.1f), 1);
                engine.AudioMgr.UpdateSyncSettings();
            }
            if (Raylib.IsKeyPressed(KeyboardKey.Two) || Raylib.IsKeyPressed(KeyboardKey.Kp2))
            {
                engine.MusicSpeed = (float)Math.Round(Math.Min(2.0f, engine.MusicSpeed + 0.1f), 1);
                engine.AudioMgr.UpdateSyncSettings();
            }
            if (Raylib.IsKeyPressed(KeyboardKey.Three) || Raylib.IsKeyPressed(KeyboardKey.Kp3))
            {
                engine.MusicSpeed = 1.0f;
                engine.AudioMgr.UpdateSyncSettings();
            }

            if (Raylib.IsKeyPressed(KeyboardKey.Space))
            {
                if (engine.TotalDuration > 0 && engine.CurrentTime >= engine.TotalDuration)
                {
                    engine.SeekTo(0);
                    engine.Resume();
                }
                else
                {
                    if (engine.IsPlaying) engine.Pause();
                    else engine.Resume();
                }
            }

            if (Raylib.IsKeyPressed(KeyboardKey.O))
            {
                RequestOpenFile?.Invoke();
            }

            Vector2 mouse = Raylib.GetMousePosition();
            bool isClick = Raylib.IsMouseButtonPressed(MouseButton.Left);
            bool isRightClick = Raylib.IsMouseButtonPressed(MouseButton.Right);
            bool isMiddleClick = Raylib.IsMouseButtonPressed(MouseButton.Middle);
            float scrollDelta = Raylib.GetMouseWheelMove();
            bool isHeld = Raylib.IsMouseButtonDown(MouseButton.Left);

            if (isClick)
            {
                if (engine.Chart.IsOjn && mouse.Y >= 431 && mouse.Y <= 455)
                {
                    if (mouse.X >= 208 && mouse.X <= 263)
                    {
                        engine.SelectOjnDifficulty(OjnDifficulty.EX);
                    }
                    else if (mouse.X >= 268 && mouse.X <= 323)
                    {
                        engine.SelectOjnDifficulty(OjnDifficulty.NX);
                    }
                    else if (mouse.X >= 328 && mouse.X <= 383)
                    {
                        engine.SelectOjnDifficulty(OjnDifficulty.HX);
                    }
                }
                else if (engine.Chart.IsOjn && mouse.X >= 202 && mouse.X <= 389 && mouse.Y >= 104 && mouse.Y <= 123)
                {
                    engine.CycleOjnDifficulty();
                }
                else if (mouse.X >= 202 && mouse.X <= 389 && mouse.Y >= 232 && mouse.Y <= 251)
                {
                    engine.CyclePlaySpeed(reverse: false);
                }
                else if (mouse.X >= 202 && mouse.X <= 389 && mouse.Y >= 264 && mouse.Y <= 283)
                {
                    engine.MusicSpeed = (engine.MusicSpeed >= 2.0f) ? 0.1f : (float)Math.Round(engine.MusicSpeed + 0.1f, 1);
                    engine.AudioMgr.UpdateSyncSettings();
                }
                else if (mouse.X >= 202 && mouse.X <= 389 && mouse.Y >= 296 && mouse.Y <= 315)
                {
                    engine.IsKeySoundEnabled = !engine.IsKeySoundEnabled;
                    engine.AudioMgr.UpdateSyncSettings();
                }
                else if (mouse.X >= 202 && mouse.X <= 389 && mouse.Y >= 328 && mouse.Y <= 347)
                {
                    engine.IsBgmEnabled = !engine.IsBgmEnabled;
                    engine.AudioMgr.UpdateSyncSettings();
                }
                else if (mouse.X >= 202 && mouse.X <= 389 && mouse.Y >= 360 && mouse.Y <= 379)
                {
                    engine.ShowEffect = !engine.ShowEffect;
                }
            }
            else if (isRightClick)
            {
                if (engine.Chart.IsOjn && mouse.X >= 202 && mouse.X <= 389 && mouse.Y >= 104 && mouse.Y <= 123)
                {
                    engine.CycleOjnDifficulty(reverse: true);
                }
                else if (mouse.X >= 202 && mouse.X <= 389 && mouse.Y >= 232 && mouse.Y <= 251)
                {
                    engine.CyclePlaySpeed(reverse: true);
                }
                else if (mouse.X >= 202 && mouse.X <= 389 && mouse.Y >= 264 && mouse.Y <= 283)
                {
                    engine.MusicSpeed = (engine.MusicSpeed <= 0.1f) ? 2.0f : (float)Math.Round(engine.MusicSpeed - 0.1f, 1);
                    engine.AudioMgr.UpdateSyncSettings();
                }
            }
            else if (isMiddleClick)
            {
                if (mouse.X >= 202 && mouse.X <= 389 && mouse.Y >= 232 && mouse.Y <= 251)
                {
                    engine.PlaySpeed = 2.0f;
                }
                else if (mouse.X >= 202 && mouse.X <= 389 && mouse.Y >= 264 && mouse.Y <= 283)
                {
                    engine.MusicSpeed = 1.0f;
                    engine.AudioMgr.UpdateSyncSettings();
                }
            }

            if (scrollDelta != 0 && mouse.X >= 202 && mouse.X <= 389)
            {
                if (mouse.Y >= 232 && mouse.Y <= 251)
                {
                    if (scrollDelta > 0)
                        engine.IncreasePlaySpeed();
                    else if (scrollDelta < 0)
                        engine.DecreasePlaySpeed();
                }
                else if (mouse.Y >= 264 && mouse.Y <= 283)
                {
                    engine.MusicSpeed = scrollDelta > 0
                        ? (float)Math.Round(Math.Min(2.0f, engine.MusicSpeed + 0.1f), 1)
                        : (float)Math.Round(Math.Max(0.1f, engine.MusicSpeed - 0.1f), 1);
                    engine.AudioMgr.UpdateSyncSettings();
                }
            }

            if (scrollDelta != 0 && engine.TotalDuration > 0)
            {
                bool isOverProgress = (mouse.X >= 200 && mouse.X <= 391 && mouse.Y >= 388 && mouse.Y <= 415);
                bool isOverPlayfield = (mouse.X >= 0 && mouse.X <= 204 && mouse.Y >= 0 && mouse.Y <= 480);

                if (isOverProgress || isOverPlayfield)
                {
                    int currentMeasure = engine.Chart.SecondsToMeasure(engine.CurrentTime);
                    int nextMeasure = Math.Clamp(currentMeasure + (scrollDelta > 0 ? 1 : -1), 0, Math.Max(0, engine.Chart.Measures.Count - 1));
                    if (scrollDelta > 0 && currentMeasure == engine.Chart.Measures.Count - 1)
                    {
                        engine.SeekTo(engine.TotalDuration);
                        _dragFrac = 1.0f;
                        _lastSeekFrac = 1.0f;
                    }
                    else if (engine.Chart.Measures.Count > nextMeasure)
                    {
                        double targetTime = engine.Chart.Measures[nextMeasure].StartTimeSeconds;
                        engine.SeekTo(targetTime);
                        _dragFrac = (float)nextMeasure / engine.Chart.Measures.Count;
                        _lastSeekFrac = _dragFrac;
                    }
                }
            }

            if (engine.TotalDuration > 0 && isClick && mouse.X >= 200 && mouse.X <= 391 && mouse.Y >= 388 && mouse.Y <= 415)
            {
                _isDraggingProgress = true;
                _wasPlayingBeforeDrag = engine.IsPlaying;
                _dragStartX = (int)mouse.X;
                _dragFrac = Math.Clamp((mouse.X - 203f) / 184f, 0f, 1f);
                _lastSeekFrac = _dragFrac;
                engine.AudioMgr.Pause();
                engine.SeekToFrac(_dragFrac);
            }
            else if (engine.TotalDuration > 0 && _isDraggingProgress)
            {
                if (isHeld)
                {
                    float newFrac = Math.Clamp((mouse.X - 203f) / 184f, 0f, 1f);
                    if (Math.Abs(newFrac - _lastSeekFrac) > 0.0005f)
                    {
                        _dragFrac = newFrac;
                        _lastSeekFrac = newFrac;
                        engine.SeekToFrac(_dragFrac);
                    }
                }
                else
                {
                    _isDraggingProgress = false;
                    _dragFrac = Math.Clamp((mouse.X - 203f) / 184f, 0f, 1f);
                    _lastSeekFrac = _dragFrac;
                    engine.IsPlaying = _wasPlayingBeforeDrag;
                    engine.SeekToFrac(_dragFrac);
                    if (_wasPlayingBeforeDrag) engine.AudioMgr.Resume();
                }
            }
        }
    }
}

