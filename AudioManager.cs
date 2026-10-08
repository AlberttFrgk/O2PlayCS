using System;
using System.Collections.Generic;

namespace O2Play
{
    public class AudioManager
    {
        public AudioEngine Audio { get; }

        public float MusicSpeed
        {
            get => Audio.MusicSpeed;
            set => Audio.MusicSpeed = value;
        }
        public bool IsKeySoundEnabled
        {
            get => Audio.IsKeySoundEnabled;
            set => Audio.IsKeySoundEnabled = value;
        }
        public bool IsBgmEnabled
        {
            get => Audio.IsBgmEnabled;
            set => Audio.IsBgmEnabled = value;
        }

        public double ActiveBgmStartSongTime { get; set; } = -1.0;

        // Background samples shorter than this are not re-started after a seek.
        private const double LongSampleSeconds = 0.25;

        public AudioManager(AudioEngine audio)
        {
            Audio = audio;
        }

        public void UpdateSyncSettings()
        {
            Audio.IsBgmEnabled = IsBgmEnabled;
            Audio.IsKeySoundEnabled = IsKeySoundEnabled;
            Audio.MusicSpeed = MusicSpeed;
        }

        public double SyncBgmClock(double currentTime)
        {
            // Software delta-time clock is the single source of truth.
            return currentTime;
        }

        public void ResyncAudio(BmsChart chart, double currentTime, bool isPlaying)
        {
            if (!isPlaying && !Audio.IsPaused) Audio.Pause();
            bool live = true;

            int bgmIndex = -1;
            double bgmStart = 0.0;
            bool anyBgmNote = false;
            BmsNote? nextBgmNote = null;

            foreach (var note in chart.Notes)
            {
                if (!note.IsKeysound || !Audio.IsBgmTrack(note.SoundIndex)) continue;
                anyBgmNote = true;

                bool hasPassed = note.IsHit || currentTime >= note.TimeSeconds;
                if (!hasPassed)
                {
                    nextBgmNote ??= note; // first trigger still ahead of us
                    continue;
                }

                double curDur = bgmIndex >= 0 ? Audio.GetBgmDuration(bgmIndex) : 0.0;
                bool curEnded = bgmIndex >= 0 && curDur > 0.0 && (note.TimeSeconds - bgmStart) >= curDur;
                if (bgmIndex != note.SoundIndex || curEnded)
                {
                    bgmIndex = note.SoundIndex;
                    bgmStart = note.TimeSeconds;
                }
            }

            double activeDur = bgmIndex >= 0 ? Audio.GetBgmDuration(bgmIndex) : 0.0;
            bool bgmActive = bgmIndex >= 0 && (activeDur <= 0.0 || (currentTime - bgmStart) < activeDur);

            if (bgmActive)
            {
                // Offset is relative to when the track was triggered, not to the latest note.
                ActiveBgmStartSongTime = bgmStart;
                Audio.PlayBgm(bgmIndex, currentTime - bgmStart, live);
            }
            else if (nextBgmNote != null)
            {
                // Before the next BGM trigger: keep the stream prepared but silent; Update() starts it on time.
                ActiveBgmStartSongTime = -1.0;
                Audio.PlayBgm(nextBgmNote.SoundIndex, 0.0, false);
            }
            else if (!anyBgmNote && Audio.BgmTrackCount == 1)
            {
                // One-sample archive that no bg note references: treat it as the whole song.
                int singleIndex = Audio.FirstBgmIndex;
                double dur = Audio.GetBgmDuration(singleIndex);
                if (dur <= 0 || currentTime < dur)
                {
                    ActiveBgmStartSongTime = 0.0;
                    Audio.PlayBgm(singleIndex, currentTime, live);
                }
                else
                {
                    ActiveBgmStartSongTime = -1.0;
                    Audio.PauseBgm();
                }
            }
            else
            {
                ActiveBgmStartSongTime = -1.0;
                Audio.PauseBgm();
            }

            // Restore active background samples
            var latestPerSample = new Dictionary<int, BmsNote>();
            foreach (var note in chart.Notes)
            {
                bool hasPassed = note.IsHit || currentTime >= note.TimeSeconds;
                if (!note.IsKeysound || !hasPassed) continue;
                if (Audio.IsBgmTrack(note.SoundIndex)) continue;

                double sampleDur = Audio.GetSampleDuration(note.SoundIndex);
                if (sampleDur < LongSampleSeconds) continue;
                if (currentTime - note.TimeSeconds >= sampleDur - 0.02) continue;

                latestPerSample[note.SoundIndex] = note;
            }

            foreach (var kvp in latestPerSample)
            {
                var n = kvp.Value;
                Audio.PlayBackgroundKeysoundAt(n.SoundIndex, currentTime - n.TimeSeconds, n.Volume, n.Pan);
            }
        }

        public void PlayKeysound(int soundIndex, float volume = 1.0f, float pan = 0.0f)
        {
            if (IsKeySoundEnabled && soundIndex >= 0)
            {
                Audio.PlayKeysound(soundIndex, volume, pan);
            }
        }

        public void PlayBackgroundKeysound(int soundIndex, float volume = 1.0f, float pan = 0.0f)
        {
            Audio.PlayBackgroundKeysound(soundIndex, volume, pan);
        }

        public void PlayBgm(int soundIndex, double offsetSeconds, bool startPlaying = true)
        {
            Audio.PlayBgm(soundIndex, offsetSeconds, startPlaying);
        }

        public void StopBgm() => Audio.StopBgm();
        public void PauseBgm() => Audio.PauseBgm();
        public void StopAll() => Audio.StopAll();
        public void StopKeysound(int soundIndex) => Audio.StopKeysound(soundIndex);
        public void Pause() => Audio.Pause();
        public void Resume() => Audio.Resume();
        public void Update() => Audio.Update();
        public bool ConsumeClockStall() => Audio.ConsumeClockStall();
    }
}
