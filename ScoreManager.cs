using System;
using System.Collections.Generic;

namespace O2Play
{
    public class ScoreManager
    {
        public int Score { get; set; }
        public int Combo { get; set; }
        public int MaxCombo { get; set; }

        public void Reset()
        {
            Score = 0;
            Combo = 0;
            MaxCombo = 0;
        }

        public void AddHit()
        {
            Combo++;
            Score += 100 + (Combo * 10);
            if (Combo > MaxCombo) MaxCombo = Combo;
        }

        public void BreakCombo()
        {
            Combo = 0;
        }

        public void RecalculateForSeek(IEnumerable<BmsNote> notes, double currentTime)
        {
            Reset();

            foreach (var note in notes)
            {
                if (note.IsKeysound)
                {
                    note.IsHit = (currentTime >= note.TimeSeconds);
                    note.IsHolding = false;
                    continue;
                }

                if (note.IsLongNote)
                {
                    if (currentTime >= note.TimeSeconds + note.DurationSeconds)
                    {
                        note.IsHit = true;
                        note.IsHolding = false;
                        AddHit();
                        AddHit();
                    }
                    else if (currentTime >= note.TimeSeconds)
                    {
                        note.IsHit = false;
                        note.IsHolding = true;
                        AddHit();
                    }
                    else
                    {
                        note.IsHit = false;
                        note.IsHolding = false;
                    }
                }
                else
                {
                    if (currentTime >= note.TimeSeconds)
                    {
                        note.IsHit = true;
                        note.IsHolding = false;
                        AddHit();
                    }
                    else
                    {
                        note.IsHit = false;
                        note.IsHolding = false;
                    }
                }
            }
        }
    }
}
