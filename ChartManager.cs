using System;
using System.Collections.Generic;

namespace O2Play
{
    // Manages chart state, timing, scroll speed, measures, and difficulty cycling.
    public class ChartManager
    {
        public BmsChart Chart { get; set; } = null!;
        public double TotalDuration { get; set; } = 30.0;

        public static readonly float[] AvailableSpeeds = { 0.5f, 1.0f, 1.5f, 2.0f, 2.5f, 3.0f, 3.5f, 4.0f, 4.5f, 5.0f, 6.0f, 8.0f };

        private float _playSpeed = UserSettings.Load().PlaySpeed;
        public float PlaySpeed
        {
            get => _playSpeed;
            set
            {
                float clamped = Math.Clamp((float)Math.Round(value, 1), 0.5f, 8.0f);
                if (Math.Abs(_playSpeed - clamped) > 0.01f)
                {
                    _playSpeed = clamped;
                    UserSettings.SavePlaySpeed(_playSpeed);
                }
            }
        }

        // Calculates note scroll speed scaled to common BPM matching Estrol/O2Game formula.
        public double BaseScrollSpeed => 192.0 * PlaySpeed * (480.0 * 0.5 * (1920.0 / 1366.0) / 112.0);

        public double ScrollSpeed
        {
            get
            {
                if (Chart == null) return BaseScrollSpeed;
                double commonBpm = Chart.GetCommonBpm();
                if (commonBpm <= 10.0 || double.IsNaN(commonBpm) || double.IsInfinity(commonBpm)) commonBpm = 140.0;
                return BaseScrollSpeed * (140.0 / commonBpm);
            }
        }

        public event Action<OjnDifficulty>? RequestChangeDifficulty;

        public ChartManager(BmsChart? chart = null)
        {
            if (chart != null)
            {
                SetChart(chart);
            }
        }

        public void SetChart(BmsChart newChart)
        {
            Chart = newChart;
            TotalDuration = CalculateTotalDuration(newChart);
        }

        public static double CalculateTotalDuration(BmsChart chart)
        {
            double duration = 30.0;
            foreach (var note in chart.Notes)
            {
                double end = note.TimeSeconds + note.DurationSeconds;
                if (end > duration) duration = end;
            }
            if (chart.Measures.Count > 0)
            {
                var lastM = chart.Measures[^1];
                if (lastM.StartTimeSeconds + lastM.DurationSeconds > duration)
                    duration = lastM.StartTimeSeconds + lastM.DurationSeconds;
            }
            return duration;
        }

        public void IncreasePlaySpeed()
        {
            for (int i = 0; i < AvailableSpeeds.Length; i++)
            {
                if (AvailableSpeeds[i] > PlaySpeed + 0.01f)
                {
                    PlaySpeed = AvailableSpeeds[i];
                    return;
                }
            }
            PlaySpeed = AvailableSpeeds[^1];
        }

        public void DecreasePlaySpeed()
        {
            for (int i = AvailableSpeeds.Length - 1; i >= 0; i--)
            {
                if (AvailableSpeeds[i] < PlaySpeed - 0.01f)
                {
                    PlaySpeed = AvailableSpeeds[i];
                    return;
                }
            }
            PlaySpeed = AvailableSpeeds[0];
        }

        public void CyclePlaySpeed(bool reverse = false)
        {
            if (!reverse)
            {
                for (int i = 0; i < AvailableSpeeds.Length; i++)
                {
                    if (AvailableSpeeds[i] > PlaySpeed + 0.01f)
                    {
                        PlaySpeed = AvailableSpeeds[i];
                        return;
                    }
                }
                PlaySpeed = AvailableSpeeds[0];
            }
            else
            {
                for (int i = AvailableSpeeds.Length - 1; i >= 0; i--)
                {
                    if (AvailableSpeeds[i] < PlaySpeed - 0.01f)
                    {
                        PlaySpeed = AvailableSpeeds[i];
                        return;
                    }
                }
                PlaySpeed = AvailableSpeeds[^1];
            }
        }

        public void CycleOjnDifficulty(bool reverse = false)
        {
            if (Chart == null || !Chart.IsOjn || Chart.CurrentDifficulty == null) return;

            int current = (int)Chart.CurrentDifficulty.Value;
            for (int step = 1; step <= 3; step++)
            {
                int next = reverse ? (current - step + 3) % 3 : (current + step) % 3;
                if (Chart.AvailableDifficulties[next])
                {
                    RequestChangeDifficulty?.Invoke((OjnDifficulty)next);
                    return;
                }
            }
        }

        public void SelectOjnDifficulty(OjnDifficulty diff)
        {
            if (Chart == null || !Chart.IsOjn) return;
            int idx = (int)diff;
            if (idx >= 0 && idx < 3 && Chart.AvailableDifficulties[idx] && Chart.CurrentDifficulty != diff)
            {
                RequestChangeDifficulty?.Invoke(diff);
            }
        }

        public float GetMeasureProgress(double currentTime)
        {
            if (Chart == null || Chart.IsDummy) return 0f;
            if (Chart.Measures.Count > 0)
            {
                int curM = Chart.SecondsToMeasure(currentTime);
                curM = Math.Clamp(curM, 0, Chart.Measures.Count - 1);
                var m = Chart.Measures[curM];
                double fracInM = m.DurationSeconds > 0
                    ? Math.Clamp((currentTime - m.StartTimeSeconds) / m.DurationSeconds, 0.0, 1.0)
                    : 0.0;
                return (float)Math.Clamp((curM + fracInM) / Chart.Measures.Count, 0.0, 1.0);
            }
            return TotalDuration > 0 ? (float)Math.Clamp(currentTime / TotalDuration, 0.0, 1.0) : 0f;
        }

        public double GetTimeFromFrac(float frac)
        {
            if (Chart == null || Chart.IsDummy) return 0.0;
            if (Chart.Measures.Count > 0)
            {
                if (frac <= 0.001f)
                {
                    return 0.0;
                }
                double mExact = Math.Clamp(frac * Chart.Measures.Count, 0.0, Chart.Measures.Count - 0.0001);
                int mIdx = (int)Math.Floor(mExact);
                double subFrac = mExact - mIdx;
                var m = Chart.Measures[mIdx];
                return m.StartTimeSeconds + (subFrac * m.DurationSeconds);
            }
            return frac * TotalDuration;
        }

        public void UpdateDummyLoop(double currentTime)
        {
            if (Chart == null || !Chart.IsDummy) return;

            double loopDuration = 8 * 192.0 * (60.0 / (140.0 * 48.0));
            double loopTicks = 8 * 192.0;

            // Continuously advance notes that have scrolled past the hit window
            foreach (var note in Chart.Notes)
            {
                double noteEnd = note.TimeSeconds + note.DurationSeconds;
                if (currentTime > noteEnd + 0.3)
                {
                    note.TimeSeconds += loopDuration;
                    note.Tick += loopTicks;
                    note.IsHit = false;
                    note.IsHolding = false;
                }
            }

            // Continuously advance measure lines that have scrolled off screen
            foreach (var measure in Chart.Measures)
            {
                if (measure.Index == 0) continue; // Initial empty measure 0 stays behind at start

                if (currentTime > measure.StartTimeSeconds + measure.DurationSeconds + 0.5)
                {
                    measure.StartTimeSeconds += loopDuration;
                    measure.StartTick += loopTicks;
                    measure.EndTick += loopTicks;
                }
            }
        }

        public static BmsChart GenerateDummyChart()
        {
            var chart = new BmsChart();
            chart.IsDummy = true;
            chart.Header.Title = "O2Viewer";
            chart.Header.Artist = "Albert Frengki";
            chart.Header.Genre = "Rhythm";
            chart.Header.InitialBpm = 140.0;
            chart.Header.PlayLevel = 0;

            // Generate structured, diverse showcase patterns (1 empty measure lead-in, then 8 measures loop)
            var rand = new Random();
            int totalPhrases = 8;
            double curTick = 192.0; // 1 measure empty lead-in at start (Measure 0 has no notes)

            for (int p = 1; p <= totalPhrases; p++)
            {
                int patternType = rand.Next(0, 6);

                switch (patternType)
                {
                    case 0:
                        // Ascending or descending stairs (8th notes = 24 ticks apart)
                        bool ascending = rand.Next(0, 2) == 0;
                        int startLane = ascending ? 1 : 7;
                        int step = ascending ? 1 : -1;
                        for (int i = 0; i < 7; i++)
                        {
                            int lane = startLane + (i * step);
                            chart.Notes.Add(new BmsNote
                            {
                                Measure = p,
                                Tick = curTick + (i * 24.0),
                                Lane = lane,
                                DurationTicks = 0,
                                Value = lane
                            });
                        }
                        // Rapid 16th note return sweep in second half
                        for (int i = 0; i < 7; i++)
                        {
                            int lane = (ascending ? 7 : 1) - (i * step);
                            chart.Notes.Add(new BmsNote
                            {
                                Measure = p,
                                Tick = curTick + 96.0 + (i * 12.0),
                                Lane = lane,
                                DurationTicks = 0,
                                Value = lane
                            });
                        }
                        break;

                    case 1:
                        // Dual edge long notes (Lanes 1 and 7) with center taps
                        chart.Notes.Add(new BmsNote
                        {
                            Measure = p,
                            Tick = curTick,
                            Lane = 1,
                            DurationTicks = 144.0, // 3 beats long note
                            Value = 1
                        });
                        chart.Notes.Add(new BmsNote
                        {
                            Measure = p,
                            Tick = curTick,
                            Lane = 7,
                            DurationTicks = 144.0, // 3 beats long note
                            Value = 7
                        });
                        // Center taps on lanes 3, 4, 5
                        int[] centerLanes = { 3, 4, 5, 4, 3, 4 };
                        for (int i = 0; i < centerLanes.Length; i++)
                        {
                            chart.Notes.Add(new BmsNote
                            {
                                Measure = p,
                                Tick = curTick + 24.0 + (i * 24.0),
                                Lane = centerLanes[i],
                                DurationTicks = 0,
                                Value = centerLanes[i]
                            });
                        }
                        break;

                    case 2:
                        // Center yellow long note (Lane 4) with outer cascades
                        chart.Notes.Add(new BmsNote
                        {
                            Measure = p,
                            Tick = curTick,
                            Lane = 4,
                            DurationTicks = 168.0, // ~3.5 beats long note on yellow
                            Value = 4
                        });
                        // Cascades on outer lanes
                        int[] outerLanes = { 2, 6, 1, 7, 3, 5, 2, 6 };
                        for (int i = 0; i < outerLanes.Length; i++)
                        {
                            chart.Notes.Add(new BmsNote
                            {
                                Measure = p,
                                Tick = curTick + 16.0 + (i * 20.0),
                                Lane = outerLanes[i],
                                DurationTicks = 0,
                                Value = outerLanes[i]
                            });
                        }
                        break;

                    case 3:
                        // Symmetrical chords (1+7, 2+6, 3+5, 4)
                        for (int beat = 0; beat < 4; beat++)
                        {
                            double bTick = curTick + (beat * 48.0);
                            if (beat == 0)
                            {
                                chart.Notes.Add(new BmsNote { Measure = p, Tick = bTick, Lane = 1, Value = 1 });
                                chart.Notes.Add(new BmsNote { Measure = p, Tick = bTick, Lane = 7, Value = 7 });
                            }
                            else if (beat == 1)
                            {
                                chart.Notes.Add(new BmsNote { Measure = p, Tick = bTick, Lane = 2, Value = 2 });
                                chart.Notes.Add(new BmsNote { Measure = p, Tick = bTick, Lane = 6, Value = 6 });
                            }
                            else if (beat == 2)
                            {
                                chart.Notes.Add(new BmsNote { Measure = p, Tick = bTick, Lane = 3, Value = 3 });
                                chart.Notes.Add(new BmsNote { Measure = p, Tick = bTick, Lane = 5, Value = 5 });
                            }
                            else
                            {
                                chart.Notes.Add(new BmsNote { Measure = p, Tick = bTick, Lane = 4, Value = 4 });
                            }
                            int offLane = rand.Next(1, 8);
                            chart.Notes.Add(new BmsNote { Measure = p, Tick = bTick + 24.0, Lane = offLane, Value = offLane });
                        }
                        break;

                    case 4:
                        // Trills (alternating between two keys)
                        int laneA = rand.Next(1, 4);
                        int laneB = laneA + rand.Next(2, 4);
                        if (laneB > 7) laneB = 7;
                        for (int i = 0; i < 8; i++)
                        {
                            int l = (i % 2 == 0) ? laneA : laneB;
                            chart.Notes.Add(new BmsNote
                            {
                                Measure = p,
                                Tick = curTick + (i * 24.0),
                                Lane = l,
                                DurationTicks = 0,
                                Value = l
                            });
                        }
                        break;

                    case 5:
                    default:
                        // Syncopated rhythm with center bass beat
                        for (int beat = 0; beat < 4; beat++)
                        {
                            double bTick = curTick + (beat * 48.0);
                            chart.Notes.Add(new BmsNote { Measure = p, Tick = bTick, Lane = 4, Value = 4 });
                            int rLane = rand.Next(1, 8);
                            if (rLane != 4)
                            {
                                bool isMiniLong = (rand.Next(0, 3) == 0);
                                chart.Notes.Add(new BmsNote
                                {
                                    Measure = p,
                                    Tick = bTick + 24.0,
                                    Lane = rLane,
                                    DurationTicks = isMiniLong ? 24.0 : 0.0,
                                    Value = rLane
                                });
                            }
                        }
                        break;
                }

                curTick += 192.0; // Advance 4 beats (1 standard measure duration)
            }

            chart.BpmChanges.Clear();
            chart.BpmChanges.Add(new BmsBpmChange { Tick = 0, Bpm = 140.0, TimeSeconds = 0 });

            double secPerTick = 60.0 / (140.0 * 48.0);

            chart.Measures.Clear();
            int totalMeasures = 1 + totalPhrases;
            for (int m = 0; m < totalMeasures; m++)
            {
                double sTick = m * 192.0;
                double eTick = (m + 1) * 192.0;
                chart.Measures.Add(new BmsMeasure
                {
                    Index = m,
                    LengthFactor = 1.0,
                    StartTick = sTick,
                    EndTick = eTick,
                    StartTimeSeconds = sTick * secPerTick,
                    DurationSeconds = 192.0 * secPerTick
                });
            }

            foreach (var note in chart.Notes)
            {
                note.TimeSeconds = note.Tick * secPerTick;
                if (note.DurationTicks > 0)
                {
                    note.DurationSeconds = note.DurationTicks * secPerTick;
                }
            }

            chart.Notes.Sort((a, b) => a.Tick.CompareTo(b.Tick));
            return chart;
        }
    }
}
