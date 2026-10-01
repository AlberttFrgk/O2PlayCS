using System;
using System.Collections.Generic;
using Raylib_cs;

namespace O2Play
{
    public class GameEngine : IDisposable
    {
        // Dedicated Manager instances
        public ChartManager ChartMgr { get; }
        public AudioManager AudioMgr { get; }
        public ScoreManager ScoreMgr { get; } = new();
        public AnimationManager Animation { get; } = new();
        public JudgeManager JudgeMgr { get; } = new();
        public InputManager InputMgr { get; } = new();
        public ResourceManager Resources { get; }
        public GraphicsManager Graphics { get; }
        public GraphicsManager Interface => Graphics;

        // Backward-compatible delegates to Managers
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

        // Exposed states for GameInterface and callers
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

        public void LoadChart(BmsChart newChart)
        {
            ChartMgr.SetChart(newChart);
            CurrentTime = 0;
            ScoreMgr.Reset();
            Animation.Reset();
            IsPlaying = true;

            Graphics?.Reset();

            AudioMgr.ActiveBgmStartSongTime = -1.0;
            AudioMgr.StopBgm();
            AudioMgr.UpdateSyncSettings();
            AudioMgr.Resume();
            SeekTo(0);
        }

        public void Update(float dt)
        {
            // A long audio (re)start blocks the main thread; the next frame's dt then includes that stall
            // while the audio only just started, which pushes notes ahead of the music.
            if (AudioMgr.ConsumeClockStall()) dt = Math.Min(dt, 1.0f / 60.0f);

            InputMgr.Update(this, JudgeMgr);

            if (!IsPlaying || InputMgr.IsDraggingProgress) return;
            CurrentTime += dt * MusicSpeed;

            // Hardware-accurate audio clock synchronization to prevent drift at any speed
            CurrentTime = AudioMgr.SyncBgmClock(CurrentTime);

            // Dummy chart: continuous seamless loop without stopping or replaying from beginning
            ChartMgr.UpdateDummyLoop(CurrentTime);

            // Stop playback when song ends (non-dummy)
            if (!Chart.IsDummy && CurrentTime >= TotalDuration + 1.0)
            {
                CurrentTime = TotalDuration;
                IsPlaying = false;
                AudioMgr.StopBgm();
                return;
            }

            // Update Judgement, Combo, and Receptor animation timers
            Animation.Update(dt, Combo);

            // Background keysounds (Lane 0) play automatically for backing audio
            JudgeMgr.ProcessBackgroundNotes(Chart, CurrentTime, AudioMgr, IsPlaying);

            // Autoplay / Hit processing (permanent preview autoplay)
            JudgeMgr.ProcessAutoplay(Chart, CurrentTime, ScoreMgr, Animation, AudioMgr);
        }

        public void SeekToFrac(float frac)
        {
            if (Chart.IsDummy) return;
            double targetTime = ChartMgr.GetTimeFromFrac(frac);
            SeekTo(targetTime);
        }

        public void SeekTo(double targetTime)
        {
            if (Chart.IsDummy) return;
            AudioMgr.StopAll();

            CurrentTime = Math.Clamp(targetTime, 0, TotalDuration);
            ScoreMgr.Reset();
            Animation.Reset();

            ScoreMgr.RecalculateForSeek(Chart.Notes, CurrentTime);

            ResyncAudio();
        }

        private void ResyncAudio()
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

