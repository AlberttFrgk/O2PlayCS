using System;
using System.Collections.Generic;

namespace O2Play
{
    public class ChartManager
    {
        public BmsChart Chart { get; set; } = null!;
        public double TotalDuration { get; set; } = 0.0;

        public static readonly float[] AvailableSpeeds = { 0.25f, 0.5f, 1.0f, 1.5f, 2.0f, 2.5f, 3.0f, 3.5f, 4.0f, 4.5f, 5.0f, 6.0f, 8.0f };

        private float _playSpeed = UserSettings.Load().PlaySpeed;
        public float PlaySpeed
        {
            get => _playSpeed;
            set
            {
                float clamped = Math.Clamp((float)Math.Round(value, 2), 0.25f, 8.0f);
                if (Math.Abs(_playSpeed - clamped) > 0.01f)
                {
                    _playSpeed = clamped;
                    UserSettings.SavePlaySpeed(_playSpeed);
                }
            }
        }

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
            if (chart == null || chart.Notes.Count == 0) return 0.0;

            double duration = 0.0;
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
            if (Chart == null || Chart.Notes.Count == 0 || TotalDuration <= 0) return 0f;
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
            if (Chart == null || Chart.Notes.Count == 0 || TotalDuration <= 0) return 0.0;
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

        public static BmsChart CreateEmptyChart()
        {
            var chart = new BmsChart();
            chart.Header.Title = "Load Chart File";
            chart.Header.Artist = "";
            chart.Header.Genre = "";
            chart.Header.InitialBpm = 120.0;
            return chart;
        }
    }
}
