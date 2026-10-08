using System;

namespace O2Play
{
    public class JudgementIndicator
    {
        public const float BaseX = 100f;
        public const float BaseY = 360f;
        public const double ScaleDuration = 0.12083333; // 7.25 frames @ 60 FPS
        public const double HoldDuration = 0.750;

        public bool IsVisible { get; set; }
        public float Scale { get; private set; } = 0.5f;
        public double ElapsedAfterScale { get; private set; }
        public double ScaleTimer { get; private set; }

        public void Play()
        {
            IsVisible = true;
            Scale = 0.5f;
            ScaleTimer = 0.0;
            ElapsedAfterScale = 0.0;
        }

        public void Update(float dt)
        {
            if (!IsVisible) return;

            if (ScaleTimer < ScaleDuration)
            {
                ScaleTimer += dt;
                float progress = Math.Clamp((float)(ScaleTimer / ScaleDuration), 0f, 1f);
                Scale = 0.5f + (0.5f * progress);
            }
            else
            {
                Scale = 1.0f;
                ElapsedAfterScale += dt;
                if (ElapsedAfterScale >= HoldDuration)
                {
                    IsVisible = false;
                }
            }
        }

        public void Reset()
        {
            IsVisible = false;
            Scale = 0.5f;
            ScaleTimer = 0.0;
            ElapsedAfterScale = 0.0;
        }
    }

    public class ComboCounter
    {
        public const float BaseX = 100f;
        public const float StartY = 260f;
        public const float TargetY = 240f;
        public const double MoveDuration = 0.06;
        public const double DelayDuration = 1.000;

        public bool IsVisible { get; set; }
        public float CurrentY { get; private set; } = TargetY;
        public int Value { get; private set; }
        public double Elapsed { get; private set; }

        public void SetCombo(int combo)
        {
            Value = combo;
            if (combo > 0)
            {
                IsVisible = true;
                Elapsed = 0.0;
                CurrentY = StartY;
            }
            else
            {
                IsVisible = false;
                Elapsed = 0.0;
            }
        }

        public void Update(float dt)
        {
            if (!IsVisible) return;

            Elapsed += dt;
            if (Elapsed < MoveDuration)
            {
                float t = (float)(Elapsed / MoveDuration);
                CurrentY = StartY + (t * (TargetY - StartY));
            }
            else if (Elapsed < MoveDuration + DelayDuration)
            {
                CurrentY = TargetY;
            }
            else
            {
                IsVisible = false;
            }
        }

        public void Reset()
        {
            IsVisible = false;
            Value = 0;
            Elapsed = 0.0;
            CurrentY = TargetY;
        }
    }

    public class AnimationManager
    {
        private readonly float[] _keyHitTimers = new float[7];
        private readonly int[] _keyHitFrames = new int[7];
        private readonly int[] _lightFrames = new int[7];
        private readonly float[] _lightTimers = new float[7];
        private readonly float[] _hitEffectTimers = new float[7];
        private readonly bool[] _laneHolding = new bool[7];
        private readonly bool[] _keyPressed = new bool[7];

        public JudgementIndicator Judgement { get; } = new();
        public ComboCounter Combo { get; } = new();

        public float[] KeyHitTimers => _keyHitTimers;
        public int[] KeyHitFrames => _keyHitFrames;
        public int[] LightFrames => _lightFrames;
        public float[] LightTimers => _lightTimers;
        public float[] HitEffectTimers => _hitEffectTimers;
        public bool[] LaneHolding => _laneHolding;
        public bool[] KeyPressed => _keyPressed;

        public bool DrawJudge
        {
            get => Judgement.IsVisible;
            set => Judgement.IsVisible = value;
        }

        public double JudgeTimer => Judgement.ScaleTimer;
        public float JudgeSize => Judgement.Scale;

        public bool DrawCombo
        {
            get => Combo.IsVisible;
            set => Combo.IsVisible = value;
        }

        public double ComboTimer => Combo.Elapsed;
        public float ComboY => Combo.CurrentY;

        public const double JudgePopDuration = JudgementIndicator.ScaleDuration;
        public const double JudgeHoldDuration = JudgementIndicator.HoldDuration;
        public const double JudgeTotalDuration = JudgePopDuration + JudgeHoldDuration;

        public const double ComboAnimDuration = ComboCounter.MoveDuration;
        public const double ComboHoldDuration = ComboCounter.DelayDuration;
        public const double ComboTotalDuration = ComboAnimDuration + ComboHoldDuration;

        public void TriggerJudge() => Judgement.Play();
        public void TriggerCombo(int combo = 1) => Combo.SetCombo(combo);
        public void StopCombo() => Combo.SetCombo(0);
        public void SetCombo(int combo) => Combo.SetCombo(combo);

        public void Reset()
        {
            Judgement.Reset();
            Combo.Reset();
            Array.Clear(_keyHitTimers, 0, 7);
            Array.Clear(_keyHitFrames, 0, 7);
            Array.Clear(_lightFrames, 0, 7);
            Array.Clear(_lightTimers, 0, 7);
            Array.Clear(_hitEffectTimers, 0, 7);
            Array.Clear(_laneHolding, 0, 7);
            Array.Clear(_keyPressed, 0, 7);
        }

        public void Update(float dt, int combo)
        {
            Judgement.Update(dt);

            if (combo <= 0 && Combo.IsVisible)
            {
                Combo.SetCombo(0);
            }
            Combo.Update(dt);

            for (int i = 0; i < 7; i++)
            {
                if (_keyHitFrames[i] > 0) _keyHitFrames[i]--;
                if (_lightFrames[i] > 0) _lightFrames[i]--;
                if (_keyHitTimers[i] > 0) _keyHitTimers[i] -= dt;
                if (_lightTimers[i] > 0) _lightTimers[i] -= dt;
                if (_hitEffectTimers[i] > 0) _hitEffectTimers[i] -= dt;
            }
        }
    }
}
