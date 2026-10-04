using System;

namespace O2Play
{
    // Manages gameplay animations, judgements, combo effects, and lane lighting.
    public class AnimationManager
    {
        private readonly float[] _keyHitTimers = new float[7];
        private readonly int[] _keyHitFrames = new int[7];
        private readonly int[] _lightFrames = new int[7];
        private readonly float[] _lightTimers = new float[7];
        private readonly float[] _hitEffectTimers = new float[7];
        private readonly bool[] _laneHolding = new bool[7];
        private readonly bool[] _keyPressed = new bool[7];

        // Judgement animation
        private bool _drawJudge = false;
        private double _judgeTimer = 0.0;
        private float _judgeSize = 0.4f;

        // Combo animation
        private bool _drawCombo = false;
        private double _comboTimer = 0.0;

        // Exposed states
        public float[] KeyHitTimers => _keyHitTimers;
        public int[] KeyHitFrames => _keyHitFrames;
        public int[] LightFrames => _lightFrames;
        public float[] LightTimers => _lightTimers;
        public float[] HitEffectTimers => _hitEffectTimers;
        public bool[] LaneHolding => _laneHolding;
        public bool[] KeyPressed => _keyPressed;

        public bool DrawJudge
        {
            get => _drawJudge;
            set => _drawJudge = value;
        }

        public double JudgeTimer
        {
            get => _judgeTimer;
            set => _judgeTimer = value;
        }

        public float JudgeSize
        {
            get => _judgeSize;
            set => _judgeSize = value;
        }

        public bool DrawCombo
        {
            get => _drawCombo;
            set => _drawCombo = value;
        }

        public double ComboTimer
        {
            get => _comboTimer;
            set => _comboTimer = value;
        }

        public void Reset()
        {
            _drawJudge = false;
            _judgeTimer = 0;
            _judgeSize = 0.4f;
            _drawCombo = false;
            _comboTimer = 0;
            Array.Clear(_keyHitTimers, 0, 7);
            Array.Clear(_keyHitFrames, 0, 7);
            Array.Clear(_lightFrames, 0, 7);
            Array.Clear(_lightTimers, 0, 7);
            Array.Clear(_hitEffectTimers, 0, 7);
            Array.Clear(_laneHolding, 0, 7);
            Array.Clear(_keyPressed, 0, 7);
        }

        public const double ComboAnimDuration = 5.0 / 60.0;

        public void TriggerJudge()
        {
            _drawJudge = true;
            _judgeTimer = 0.0;
            _judgeSize = 0.4f;
        }

        public void TriggerCombo()
        {
            _drawCombo = true;
            // Debounce chord micro-intervals (< 15ms) to prevent violent dual-trigger flickering
            if (_comboTimer < 0.015 && _comboTimer > 0.0)
            {
                return;
            }
            _comboTimer = 0.0;
        }

        public void StopCombo()
        {
            _drawCombo = false;
            _comboTimer = 0.0;
        }

        public void Update(float dt, int combo)
        {
            // Update Judgement animation
            if (_drawJudge)
            {
                _judgeSize = Math.Clamp(_judgeSize + (dt * 6.0f), 0.4f, 1.0f);
                if ((_judgeTimer += dt) > 0.60)
                {
                    _drawJudge = false;
                }
            }

            // Update Combo animation
            if (_drawCombo && combo > 0)
            {
                _comboTimer += dt;
                if (_comboTimer >= 1.0)
                {
                    _comboTimer = 0.0;
                    _drawCombo = false;
                }
            }

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
