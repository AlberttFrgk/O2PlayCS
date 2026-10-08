using System;
using System.Collections.Generic;

namespace O2Play
{
    public class JudgeManager
    {
        public const double HitWindowSeconds = 0.180;
        public const double EarlyReleaseToleranceSeconds = 0.080;

        public void ProcessBackgroundNotes(BmsChart chart, double currentTime, AudioManager audioMgr, bool isPlaying)
        {
            if (chart == null) return;

            foreach (var note in chart.Notes)
            {
                int laneIdx = note.Lane - 1;

                if (note.IsKeysound || laneIdx < 0 || laneIdx > 6)
                {
                    if (!note.IsHit && currentTime >= note.TimeSeconds)
                    {
                        note.IsHit = true;
                        if (audioMgr.Audio.IsBgmTrack(note.SoundIndex))
                        {
                            double dur = audioMgr.Audio.GetBgmDuration(note.SoundIndex);
                            double offset = Math.Max(0, currentTime - note.TimeSeconds);
                            if (dur <= 0.0 || offset < dur)
                            {
                                if (audioMgr.Audio.CurrentBgmIndex != note.SoundIndex || !audioMgr.Audio.IsBgmPlaying)
                                {
                                    audioMgr.ActiveBgmStartSongTime = note.TimeSeconds;
                                    audioMgr.PlayBgm(note.SoundIndex, offset, isPlaying);
                                }
                            }
                        }
                        else
                        {
                            audioMgr.PlayBackgroundKeysound(note.SoundIndex, note.Volume, note.Pan);
                        }
                    }
                }
            }
        }

        public void ProcessAutoplay(BmsChart chart, double currentTime, ScoreManager scoreMgr, AnimationManager animation, AudioManager audioMgr)
        {
            if (chart == null) return;

            for (int i = 0; i < chart.Notes.Count; i++)
            {
                var note = chart.Notes[i];
                int laneIdx = note.Lane - 1;
                if (note.IsKeysound || laneIdx < 0 || laneIdx > 6) continue;

                if (note.IsLongNote)
                {
                    if (!note.IsHolding && !note.IsHit && currentTime >= note.TimeSeconds && currentTime < note.TimeSeconds + note.DurationSeconds)
                    {
                        note.IsHolding = true;
                        scoreMgr.AddHit();

                        animation.TriggerJudge();
                        animation.TriggerCombo();

                        animation.KeyHitTimers[laneIdx] = 0.12f;
                        animation.LaneHolding[laneIdx] = true;

                        if (audioMgr.IsKeySoundEnabled && note.SoundIndex >= 0)
                        {
                            audioMgr.PlayKeysound(note.SoundIndex, note.Volume, note.Pan);
                        }
                    }
                    else if (note.IsHolding && currentTime < note.TimeSeconds + note.DurationSeconds)
                    {
                        animation.KeyHitTimers[laneIdx] = 0.1f;
                    }
                    else if (note.IsHolding && currentTime >= note.TimeSeconds + note.DurationSeconds)
                    {
                        note.IsHolding = false;
                        note.IsHit = true;
                        scoreMgr.AddHit();

                        animation.TriggerJudge();
                        animation.TriggerCombo();

                        animation.LaneHolding[laneIdx] = false;
                        animation.LightTimers[laneIdx] = 0;
                        animation.HitEffectTimers[laneIdx] = 0.15f;
                    }
                    else if (!note.IsHit && currentTime >= note.TimeSeconds + note.DurationSeconds)
                    {
                        if (note.IsHolding)
                        {
                            note.IsHolding = false;
                            scoreMgr.AddHit();
                        }
                        else
                        {
                            scoreMgr.AddHit();
                            scoreMgr.AddHit();
                        }
                        note.IsHit = true;
                        animation.TriggerJudge();
                        animation.TriggerCombo();
                        animation.LaneHolding[laneIdx] = false;
                    }
                }
                else
                {
                    if (!note.IsHit && currentTime >= note.TimeSeconds)
                    {
                        note.IsHit = true;
                        scoreMgr.AddHit();

                        animation.TriggerJudge();
                        animation.TriggerCombo();

                        // Normal note: human-like key tap hold duration (~80ms),
                        // releasing early if the next note in the same lane arrives sooner
                        float holdTime = 0.060f;
                        float speed = Math.Max(0.1f, audioMgr.MusicSpeed);
                        for (int nextIdx = i + 1; nextIdx < chart.Notes.Count; nextIdx++)
                        {
                            var next = chart.Notes[nextIdx];
                            if (next.Lane == note.Lane && !next.IsKeysound)
                            {
                                double realGap = (next.TimeSeconds - note.TimeSeconds) / speed;
                                if (realGap > 0.001)
                                {
                                    holdTime = Math.Clamp((float)(realGap * 0.75), 0.030f, 0.060f);
                                }
                                break;
                            }
                        }

                        animation.KeyHitTimers[laneIdx] = holdTime;
                        animation.LightTimers[laneIdx] = holdTime;
                        animation.KeyHitFrames[laneIdx] = 0;
                        animation.LightFrames[laneIdx] = 0;
                        animation.HitEffectTimers[laneIdx] = 0.15f;

                        if (audioMgr.IsKeySoundEnabled && note.SoundIndex >= 0)
                        {
                            audioMgr.PlayKeysound(note.SoundIndex, note.Volume, note.Pan);
                        }
                    }
                }
            }
        }

        public void ProcessManualMisses(BmsChart chart, double currentTime, ScoreManager scoreMgr, AnimationManager animation)
        {
            if (chart == null) return;

            foreach (var note in chart.Notes)
            {
                int laneIdx = note.Lane - 1;
                if (note.IsKeysound || laneIdx < 0 || laneIdx > 6) continue;

                if (!note.IsHit && !note.IsHolding)
                {
                    double missTime = note.IsLongNote
                        ? (note.TimeSeconds + note.DurationSeconds)
                        : (note.TimeSeconds + HitWindowSeconds);

                    if (currentTime > missTime)
                    {
                        note.IsHit = true;
                        if (scoreMgr.Combo > 0)
                        {
                            scoreMgr.BreakCombo();
                            animation.StopCombo();
                        }
                    }
                }
            }
        }

        public void HandleKeyDown(BmsChart chart, double currentTime, int laneIdx, ScoreManager scoreMgr, AnimationManager animation, AudioManager audioMgr)
        {
            if (chart == null || laneIdx < 0 || laneIdx > 6) return;

            animation.KeyHitTimers[laneIdx] = 0.12f;

            BmsNote? target = null;
            double minTime = double.MaxValue;
            foreach (var note in chart.Notes)
            {
                if (note.Lane == laneIdx + 1 && !note.IsHit && !note.IsHolding)
                {
                    if (note.TimeSeconds < minTime)
                    {
                        minTime = note.TimeSeconds;
                        target = note;
                    }
                }
            }

            if (target != null && Math.Abs(currentTime - target.TimeSeconds) <= HitWindowSeconds)
            {
                if (target.IsLongNote)
                {
                    target.IsHolding = true;
                    animation.LaneHolding[laneIdx] = true;
                    scoreMgr.AddHit();

                    animation.TriggerJudge();
                    animation.TriggerCombo();

                    animation.LightTimers[laneIdx] = 0.100f;
                    animation.HitEffectTimers[laneIdx] = 0.15f;

                    if (audioMgr.IsKeySoundEnabled && target.SoundIndex >= 0)
                    {
                        audioMgr.PlayKeysound(target.SoundIndex, target.Volume, target.Pan);
                    }
                }
                else
                {
                    target.IsHit = true;
                    scoreMgr.AddHit();

                    animation.TriggerJudge();
                    animation.TriggerCombo();

                    animation.LightTimers[laneIdx] = 0.100f;
                    animation.HitEffectTimers[laneIdx] = 0.15f;

                    if (audioMgr.IsKeySoundEnabled && target.SoundIndex >= 0)
                    {
                        audioMgr.PlayKeysound(target.SoundIndex, target.Volume, target.Pan);
                    }
                }
            }
        }

        public void HandleKeyHold(BmsChart chart, double currentTime, int laneIdx, ScoreManager scoreMgr, AnimationManager animation, AudioManager? audioMgr = null)
        {
            if (chart == null || laneIdx < 0 || laneIdx > 6) return;

            foreach (var note in chart.Notes)
            {
                if (note.Lane == laneIdx + 1 && note.IsHolding)
                {
                    animation.LaneHolding[laneIdx] = true;
                    animation.KeyHitTimers[laneIdx] = 0.1f;

                    if (currentTime >= note.TimeSeconds + note.DurationSeconds)
                    {
                        note.IsHolding = false;
                        note.IsHit = true;
                        scoreMgr.AddHit();

                        animation.TriggerJudge();
                        animation.TriggerCombo();

                        animation.LaneHolding[laneIdx] = false;
                        animation.HitEffectTimers[laneIdx] = 0.15f;
                    }
                }
            }
        }

        public void HandleKeyUp(BmsChart chart, double currentTime, int laneIdx, ScoreManager scoreMgr, AnimationManager animation, AudioManager? audioMgr = null)
        {
            if (chart == null || laneIdx < 0 || laneIdx > 6) return;

            foreach (var note in chart.Notes)
            {
                if (note.Lane == laneIdx + 1 && note.IsHolding)
                {
                    note.IsHolding = false;
                    note.IsHit = true;
                    animation.LaneHolding[laneIdx] = false;

                    if (currentTime < note.TimeSeconds + note.DurationSeconds - EarlyReleaseToleranceSeconds)
                    {
                        if (scoreMgr.Combo > 0)
                        {
                            scoreMgr.BreakCombo();
                            animation.StopCombo();
                        }
                    }
                    else
                    {
                        scoreMgr.AddHit();
                        animation.TriggerJudge();
                        animation.TriggerCombo();
                        animation.HitEffectTimers[laneIdx] = 0.15f;
                    }
                }
            }
        }
    }
}
