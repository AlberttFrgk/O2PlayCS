using System;
using System.Collections.Generic;
using Raylib_cs;

namespace O2Play
{
    public class GameEngine : IDisposable
    {
        public ChartManager ChartMgr { get; }
        public AudioManager AudioMgr { get; }
        public ScoreManager ScoreMgr { get; } = new();
        public AnimationManager Animation { get; } = new();
        public JudgeManager JudgeMgr { get; } = new();
        public InputManager InputMgr { get; } = new();
        public ResourceManager Resources { get; }
        public GraphicsManager Graphics { get; }
        public GraphicsManager Interface => Graphics;

        public BmsChart Chart { get => ChartMgr.Chart; private set => ChartMgr.Chart = value; }
        public AudioEngine Audio => AudioMgr.Audio;

        public double CurrentTime { get; private set; }
        public double TotalDuration { get => ChartMgr.TotalDuration; private set => ChartMgr.TotalDuration = value; }
        public bool IsPlaying { get; set; }

        public int Score { get => ScoreMgr.Score; private set => ScoreMgr.Score = value; }
        public int Combo { get => ScoreMgr.Combo; private set => ScoreMgr.Combo = value; }
        public int MaxCombo { get => ScoreMgr.MaxCombo; private set => ScoreMgr.MaxCombo = value; }

        public static readonly float[] AvailableSpeeds = ChartManager.AvailableSpeeds;

        public void IncreasePlaySpeed() => ChartMgr.IncreasePlaySpeed();
        public void DecreasePlaySpeed() => ChartMgr.DecreasePlaySpeed();
        public void CyclePlaySpeed(bool reverse = false) => ChartMgr.CyclePlaySpeed(reverse);

        public float PlaySpeed
        {
            get => ChartMgr.PlaySpeed;
            set => ChartMgr.PlaySpeed = value;
        }

        public float MusicSpeed
        {
            get => AudioMgr.MusicSpeed;
            set => AudioMgr.MusicSpeed = value;
        }

        public bool IsKeySoundEnabled
        {
            get => AudioMgr.IsKeySoundEnabled;
            set => AudioMgr.IsKeySoundEnabled = value;
        }

        public bool IsBgmEnabled
        {
            get => AudioMgr.IsBgmEnabled;
            set => AudioMgr.IsBgmEnabled = value;
        }

        public bool IsAutoPlay { get; set; } = true;
        private bool _showEffect = UserSettings.Load().ShowEffect;
        public bool ShowEffect
        {
            get => _showEffect;
            set
            {
                if (_showEffect != value)
                {
                    _showEffect = value;
                    UserSettings.SaveShowEffect(_showEffect);
                }
            }
        }

        public double BaseScrollSpeed => ChartMgr.BaseScrollSpeed;
        public double ScrollSpeed => ChartMgr.ScrollSpeed;

        public Texture2D TexPlayfield => Resources.TexPlayfield;
        public Texture2D TexKeyDown => Resources.TexKeyDown;
        public Texture2D TexNote => Resources.TexNote;
        public Texture2D TexCombo => Resources.TexCombo;
        public Texture2D TexHitEffect => Resources.TexHitEffect;
        public Texture2D TexLongEffect => Resources.TexLongEffect;
        public Texture2D TexJudgement => Resources.TexJudgement;
        public Texture2D TexLight => Resources.TexLight;
        public Texture2D TexTargetBar => Resources.TexTargetBar;

        public bool IsWindowFocused { get; set; } = true;

        public float[] KeyHitTimers => Animation.KeyHitTimers;
        public int[] KeyHitFrames => Animation.KeyHitFrames;
        public int[] LightFrames => Animation.LightFrames;
        public float[] LightTimers => Animation.LightTimers;
        public float[] HitEffectTimers => Animation.HitEffectTimers;
        public bool[] LaneHolding => Animation.LaneHolding;
        public bool[] KeyPressed => Animation.KeyPressed;
        public bool DrawJudge => Animation.DrawJudge;
        public float JudgeSize => Animation.JudgeSize;
        public bool DrawCombo => Animation.DrawCombo;
        public double ComboTimer => Animation.ComboTimer;
        public float ComboY => Animation.ComboY;

        public bool IsDraggingProgress => InputMgr.IsDraggingProgress;
        public float DragFrac => InputMgr.DragFrac;

        public event Action? RequestOpenFile;
        public event Action<OjnDifficulty>? RequestChangeDifficulty;

        public void CycleOjnDifficulty(bool reverse = false) => ChartMgr.CycleOjnDifficulty(reverse);
        public void SelectOjnDifficulty(OjnDifficulty diff) => ChartMgr.SelectOjnDifficulty(diff);

        public GameEngine(BmsChart chart, AudioEngine audio, ResourceManager? resources = null)
        {
            ChartMgr = new ChartManager(chart);
            ChartMgr.RequestChangeDifficulty += diff => RequestChangeDifficulty?.Invoke(diff);
            AudioMgr = new AudioManager(audio);
            Resources = resources ?? new ResourceManager();
            Resources.LoadGameAssets();
            Graphics = new GraphicsManager(this);

            InputMgr.RequestOpenFile += () => RequestOpenFile?.Invoke();

            LoadChart(chart);
        }

        private double _lastNoteTime = 0.0;

        public void LoadChart(BmsChart newChart)
        {
            ChartMgr.SetChart(newChart);
            CurrentTime = 0;
            ScoreMgr.Reset();
            Animation.Reset();
            IsPlaying = newChart.Notes.Count > 0;

            _lastNoteTime = 0.0;
            if (newChart.Notes.Count > 0)
            {
                foreach (var note in newChart.Notes)
                {
                    double end = note.TimeSeconds + note.DurationSeconds;
                    if (end > _lastNoteTime) _lastNoteTime = end;
                }
            }

            Graphics?.Reset();

            AudioMgr.ActiveBgmStartSongTime = -1.0;
            AudioMgr.StopBgm();
            AudioMgr.UpdateSyncSettings();
            if (IsPlaying)
            {
                AudioMgr.Resume();
                SeekTo(0);
            }
        }

        public void Pause()
        {
            IsPlaying = false;
            AudioMgr.Pause();
        }

        public bool CanResume()
        {
            if (Chart == null || Chart.Notes.Count == 0) return false;
            if (TotalDuration > 0 && CurrentTime >= TotalDuration) return false;
            return true;
        }

        public void Resume()
        {
            if (!CanResume())
            {
                IsPlaying = false;
                AudioMgr.StopAll();
                AudioMgr.StopBgm();
                return;
            }

            IsPlaying = true;
            AudioMgr.Resume();
        }

        public void Update(float dt)
        {
            // Protect against huge delta spike if the window was frozen/dragged
            dt = Math.Clamp(dt, 0.0001f, 0.1f);

            InputMgr.Update(this, JudgeMgr);

            if (!IsPlaying || InputMgr.IsDraggingProgress) return;

            // Master clock advances purely via delta-time and music speed
            CurrentTime += dt * MusicSpeed;

            // Synchronize with active BGM stream so window dragging or hitches never cause audio/note drift
            double bgmPos = AudioMgr.Audio.BgmPositionSeconds;
            if (bgmPos >= 0.0 && AudioMgr.ActiveBgmStartSongTime >= 0.0)
            {
                double targetSongTime = AudioMgr.ActiveBgmStartSongTime + bgmPos;
                if (Math.Abs(CurrentTime - targetSongTime) > 0.02 && targetSongTime >= CurrentTime - 0.25)
                {
                    CurrentTime = targetSongTime;
                }
            }

            if (TotalDuration > 0 && CurrentTime >= TotalDuration)
            {
                CurrentTime = TotalDuration;
                IsPlaying = false;
                AudioMgr.StopAll();
                AudioMgr.StopBgm();
                return;
            }

            Animation.Update(dt, Combo);

            // Background keysounds (Lane 0) play automatically for backing audio
            JudgeMgr.ProcessBackgroundNotes(Chart, CurrentTime, AudioMgr, IsPlaying);
            JudgeMgr.ProcessAutoplay(Chart, CurrentTime, ScoreMgr, Animation, AudioMgr);
        }

        public void SeekToFrac(float frac)
        {
            if (Chart.Notes.Count == 0) return;
            double targetTime = ChartMgr.GetTimeFromFrac(frac);
            SeekTo(targetTime);
        }

        public void SeekTo(double targetTime)
        {
            if (Chart.Notes.Count == 0) return;
            AudioMgr.StopAll();

            CurrentTime = Math.Clamp(targetTime, 0, TotalDuration);
            ScoreMgr.Reset();
            Animation.Reset();

            ScoreMgr.RecalculateForSeek(Chart.Notes, CurrentTime);

            ResyncAudio();
        }

        public void ResyncAudio()
        {
            AudioMgr.ResyncAudio(Chart, CurrentTime, IsPlaying);
        }

        public void Draw() => Graphics.Draw();

        public float GetMeasureProgress()
        {
            return ChartMgr.GetMeasureProgress(CurrentTime);
        }

        public void Dispose()
        {
            Resources.Dispose();
            Graphics.Dispose();
        }
    }
}

