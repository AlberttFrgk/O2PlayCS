using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using ManagedBass;

namespace O2Play
{
    public class AudioEngine : IDisposable
    {
        private readonly Dictionary<int, int> _sampleHandles = new();
        private readonly Dictionary<int, float> _sampleFrequencies = new();
        private readonly Dictionary<long, int> _contentHashToSample = new();
        private readonly Dictionary<int, string> _bgmTracks = new();
        private readonly Dictionary<int, byte[]> _bgmBytes = new();
        private readonly Dictionary<int, double> _bgmDurations = new();
        private readonly List<int> _activeChannels = new();
        private readonly List<int> _activeBgmChannels = new();
        private readonly List<int> _activeKeyChannels = new();
        private readonly Dictionary<int, float> _activeChannelFreqs = new();
        private readonly Dictionary<int, float> _activeChannelBaseVols = new();

        public class ArchiveAudioCache
        {
            public string ArchivePath { get; set; } = string.Empty;
            public DateTime LastWriteTime { get; set; }
            public Dictionary<int, int> SampleHandles { get; } = new();
            public Dictionary<int, float> SampleFrequencies { get; } = new();
            public Dictionary<long, int> ContentHashToSample { get; } = new();
            public Dictionary<int, byte[]> BgmBytes { get; } = new();
            public Dictionary<int, double> BgmDurations { get; } = new();
            public bool IsSingleSampleBgm { get; set; }
        }

        private static readonly Dictionary<string, ArchiveAudioCache> _cachedArchives = new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> _archiveLruOrder = new();
        private const int MAX_CACHED_ARCHIVES = 4;

        public string CurrentArchiveSource { get; private set; } = string.Empty;
        public DateTime CurrentArchiveTime { get; private set; }
        public int LoadedSampleCount => _sampleHandles.Count + _bgmBytes.Count;
        public int BgmTrackCount => _bgmTracks.Count + _bgmBytes.Count;
        public int FirstBgmIndex
        {
            get
            {
                foreach (int k in _bgmTracks.Keys) return k;
                foreach (int k in _bgmBytes.Keys) return k;
                return -1;
            }
        }

        public bool HasArchiveLoaded(string archivePath)
        {
            if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath)) return false;
            DateTime writeTime = File.GetLastWriteTimeUtc(archivePath);

            if (string.Equals(CurrentArchiveSource, archivePath, StringComparison.OrdinalIgnoreCase)
                && CurrentArchiveTime == writeTime
                && (_sampleHandles.Count > 0 || _bgmBytes.Count > 0))
            {
                return true;
            }

            lock (_cachedArchives)
            {
                return _cachedArchives.TryGetValue(archivePath, out var cached)
                    && cached.LastWriteTime == writeTime
                    && (cached.SampleHandles.Count > 0 || cached.BgmBytes.Count > 0);
            }
        }

        public void SetArchiveLoaded(string archivePath, DateTime lastWriteTime)
        {
            CurrentArchiveSource = archivePath;
            CurrentArchiveTime = lastWriteTime;
        }

        private int _currentBgmIndex = -1;
        private int _bgmStream = 0;
        private float _bgmBaseFreq = 44100f;
        private GCHandle _pinnedBgmHandle;

        private bool _isBgmEnabled = true;
        private bool _isKeySoundEnabled = true;
        private bool _isPaused = false;
        private float _musicSpeed = 1.0f;
        private bool _disposed = false;
        // True when the current BGM stream is supposed to be audible at the current song position
        // (it may still be held back while the device is paused). Resume() only restarts BGM when this is set.
        private bool _bgmArmed = false;

        // BGM (re)positioning requested while the device was paused; applied by Resume().
        private (int Index, double Offset, bool Play)? _pendingBgm = null;

        // Background samples that were re-positioned (seek) while the device was paused; started by Resume().
        private readonly List<(int Index, double Offset, float Volume, float Pan)> _pendingSamples = new();

        // Sample length in natural (1.0x) seconds, per sample index.
        private readonly Dictionary<int, double> _sampleDurationCache = new();

        public int CurrentBgmIndex => _currentBgmIndex;
        public bool IsPaused => _isPaused;
        public bool IsBgmPlaying => _bgmStream != 0 && Bass.ChannelIsActive(_bgmStream) == PlaybackState.Playing;

        public double OutputLatencySeconds => 0.0;

        public double BgmPositionSeconds
        {
            get
            {
                if (_bgmStream != 0 && Bass.ChannelIsActive(_bgmStream) == PlaybackState.Playing)
                {
                    long pos = Bass.ChannelGetPosition(_bgmStream);
                    if (pos >= 0)
                    {
                        return Bass.ChannelBytes2Seconds(_bgmStream, pos);
                    }
                }
                return -1.0;
            }
        }

        public float MusicSpeed
        {
            get => _musicSpeed;
            set
            {
                _musicSpeed = Math.Clamp(value, 0.1f, 3.0f);
                if (_bgmStream != 0 && _bgmBaseFreq > 0f)
                {
                    Bass.ChannelSetAttribute(_bgmStream, ChannelAttribute.Frequency, _bgmBaseFreq * _musicSpeed);
                }

                // Update playing keysound frequencies in real-time
                lock (_activeChannels)
                {
                    for (int i = _activeChannels.Count - 1; i >= 0; i--)
                    {
                        int ch = _activeChannels[i];
                        if (Bass.ChannelIsActive(ch) == PlaybackState.Playing)
                        {
                            if (_activeChannelFreqs.TryGetValue(ch, out float baseFreq))
                            {
                                Bass.ChannelSetAttribute(ch, ChannelAttribute.Frequency, baseFreq * _musicSpeed);
                            }
                        }
                        else
                        {
                            _activeChannels.RemoveAt(i);
                            _activeBgmChannels.Remove(ch);
                            _activeKeyChannels.Remove(ch);
                            _activeChannelFreqs.Remove(ch);
                            _activeChannelBaseVols.Remove(ch);
                        }
                    }
                }
            }
        }

        public float Pitch => Math.Clamp((float)(Math.Log(_musicSpeed) / Math.Log(2.0)), -1.0f, 1.0f);

        public bool IsBgmEnabled
        {
            get => _isBgmEnabled;
            set
            {
                _isBgmEnabled = value;
                if (_bgmStream != 0)
                {
                    Bass.ChannelSetAttribute(_bgmStream, ChannelAttribute.Volume, _isBgmEnabled ? 1.0f : 0.0f);
                }

                lock (_activeChannels)
                {
                    foreach (int ch in _activeBgmChannels)
                    {
                        if (Bass.ChannelIsActive(ch) == PlaybackState.Playing)
                        {
                            float baseVol = _activeChannelBaseVols.TryGetValue(ch, out float v) ? v : 1.0f;
                            Bass.ChannelSetAttribute(ch, ChannelAttribute.Volume, _isBgmEnabled ? Math.Clamp(baseVol, 0.0f, 1.0f) : 0.0f);
                        }
                    }
                }
            }
        }

        public bool IsKeySoundEnabled
        {
            get => _isKeySoundEnabled;
            set
            {
                _isKeySoundEnabled = value;
                lock (_activeChannels)
                {
                    foreach (int ch in _activeKeyChannels)
                    {
                        if (Bass.ChannelIsActive(ch) == PlaybackState.Playing)
                        {
                            float baseVol = _activeChannelBaseVols.TryGetValue(ch, out float v) ? v : 1.0f;
                            Bass.ChannelSetAttribute(ch, ChannelAttribute.Volume, _isKeySoundEnabled ? Math.Clamp(baseVol, 0.0f, 1.0f) : 0.0f);
                        }
                    }
                }
            }
        }

        public AudioEngine()
        {
            try
            {
                // Low latency settings for rhythm precision (20ms buffer, 5ms update)
                Bass.Configure(Configuration.DeviceBufferLength, 20);
                Bass.Configure(Configuration.UpdatePeriod, 5);
                Bass.Configure((Configuration)50, 1); // BASS_CONFIG_DEV_NONSTOP
                Bass.Configure(Configuration.FloatDSP, true);

                if (!Bass.Init(-1, 44100, DeviceInitFlags.Default, IntPtr.Zero))
                {
                    if (Bass.LastError != Errors.Already)
                    {
                        Logger.Warn($"[AudioEngine] Bass.Init note: {Bass.LastError}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[AudioEngine] Exception during Bass.Init: {ex.Message}");
            }

            PrepareBass();
        }

        private void PrepareBass()
        {
            try
            {
                // Pre-warm device thread to eliminate startup stutter
                byte[] bootWav = CreateToneWav(440.0, 0.05, 0.0);
                int bootSample = Bass.SampleLoad(bootWav, 0, bootWav.Length, 1, BassFlags.Default);
                if (bootSample != 0)
                {
                    int ch = Bass.SampleGetChannel(bootSample);
                    if (ch != 0)
                    {
                        Bass.ChannelSetAttribute(ch, ChannelAttribute.Volume, 0f);
                        Bass.ChannelPlay(ch, false);
                    }
                    Bass.SampleFree(bootSample);
                }
            }
            catch { }
        }

        private void FreeSampleIfUnused(int sampleToFree, int excludingIndex)
        {
            if (sampleToFree == 0) return;
            foreach (var kvp in _sampleHandles)
            {
                if (kvp.Key != excludingIndex && kvp.Value == sampleToFree)
                {
                    return; // Still in use by another index
                }
            }
            Bass.SampleFree(sampleToFree);
        }

        // Releases any existing sample at this index, stores the new sample handle, and caches its base frequency.
        private void RegisterSample(int index, int sample)
        {
            if (_sampleHandles.TryGetValue(index, out int oldSample) && oldSample != sample)
            {
                FreeSampleIfUnused(oldSample, index);
            }
            _sampleHandles[index] = sample;
            _sampleDurationCache.Remove(index);

            var info = new SampleInfo();
            if (Bass.SampleGetInfo(sample, info))
            {
                if ((info.Flags & BassFlags.Loop) != 0)
                {
                    info.Flags &= ~BassFlags.Loop;
                    Bass.SampleSetInfo(sample, info);
                }
                _sampleFrequencies[index] = info.Frequency;
            }
            else
            {
                _sampleFrequencies[index] = 44100f;
            }
        }

        public static byte[] CreateToneWav(double frequencyHz, double durationSec, double volume = 0.35)
        {
            int sampleRate = 44100;
            int numSamples = (int)(sampleRate * durationSec);
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            // Canonical 44-byte WAV header
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + (numSamples * 2));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1); // PCM
            writer.Write((short)1); // Mono
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(numSamples * 2);

            for (int i = 0; i < numSamples; i++)
            {
                double t = (double)i / sampleRate;
                double env = Math.Exp(-t * 9.0);
                if (t < 0.002) env *= (t / 0.002);
                double sample = (Math.Sin(2.0 * Math.PI * frequencyHz * t) + 0.25 * Math.Sin(4.0 * Math.PI * frequencyHz * t)) * env * volume;
                short val = (short)Math.Clamp((int)(sample * 32767.0), -32768, 32767);
                writer.Write(val);
            }

            return ms.ToArray();
        }

        // Exact match only. (The old "single sample archive => every index is BGM" shortcut made playable notes and
        // unrelated bg notes look like BGM triggers, which broke seeking on one-sample OJMs.)
        public bool IsBgmTrack(int index) => _bgmTracks.ContainsKey(index) || _bgmBytes.ContainsKey(index);

        // Length of a loaded polyphonic sample in natural seconds (0 if unknown).
        public double GetSampleDuration(int index)
        {
            if (_sampleDurationCache.TryGetValue(index, out double cached)) return cached;

            double seconds = 0.0;
            if (_sampleHandles.TryGetValue(index, out int handle))
            {
                var info = new SampleInfo();
                if (Bass.SampleGetInfo(handle, info) && info.Frequency > 0 && info.Channels > 0)
                {
                    int bytesPerSample = (info.Flags & BassFlags.Byte) != 0 ? 1
                                       : (info.Flags & BassFlags.Float) != 0 ? 4 : 2;
                    seconds = info.Length / (double)((long)info.Frequency * info.Channels * bytesPerSample);
                }
            }
            _sampleDurationCache[index] = seconds;
            return seconds;
        }
        public double GetBgmDuration(int index) => _bgmDurations.TryGetValue(index, out double d) ? d : 0.0;

        public void LoadSound(int index, string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return;

                // Probe audio duration using BASS decode stream
                int probe = Bass.CreateStream(filePath, 0, 0, BassFlags.Decode);
                if (probe != 0)
                {
                    long lenBytes = Bass.ChannelGetLength(probe);
                    double totalSec = Bass.ChannelBytes2Seconds(probe, lenBytes);
                    Bass.StreamFree(probe);

                    // If duration is 15 seconds or longer, register as streaming BGM
                    if (totalSec >= 15.0)
                    {
                        _bgmTracks[index] = filePath;
                        _bgmDurations[index] = totalSec;
                        return;
                    }
                }

                // Load short audio (WAV, MP3, OGG) as low-latency sound sample
                int sample = Bass.SampleLoad(filePath, 0, 0, 32, BassFlags.SampleOverrideLongestPlaying);
                if (sample != 0)
                {
                    RegisterSample(index, sample);
                }
                else
                {
                    Logger.Warn($"[AudioEngine] Bass.SampleLoad failed for {filePath}: {Bass.LastError}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[AudioEngine] Error loading sound {index} ({filePath}): {ex.Message}");
            }
        }

        private static long ComputeFastHash(byte[] data)
        {
            unchecked
            {
                long hash = (long)2166136261L;
                hash = (hash ^ data.Length) * 16777619L;
                if (data.Length <= 1024)
                {
                    for (int i = 0; i < data.Length; i++)
                    {
                        hash = (hash ^ data[i]) * 16777619L;
                    }
                }
                else
                {
                    for (int i = 0; i < 256; i++) hash = (hash ^ data[i]) * 16777619L;
                    int mid = data.Length / 2;
                    for (int i = 0; i < 256; i++) hash = (hash ^ data[mid + i]) * 16777619L;
                    int end = data.Length - 256;
                    for (int i = 0; i < 256; i++) hash = (hash ^ data[end + i]) * 16777619L;
                }
                return hash;
            }
        }

        public void LoadSoundFromBytes(int index, byte[] data, string name = "", bool forceBgm = false)
        {
            if (data == null || data.Length == 0) return;

            try
            {
                if (forceBgm)
                {
                    double totalSec = 0.0;
                    GCHandle pin = GCHandle.Alloc(data, GCHandleType.Pinned);
                    try
                    {
                        int probe = Bass.CreateStream(pin.AddrOfPinnedObject(), 0, data.Length, BassFlags.Decode);
                        if (probe != 0)
                        {
                            long lenBytes = Bass.ChannelGetLength(probe);
                            totalSec = Bass.ChannelBytes2Seconds(probe, lenBytes);
                            Bass.StreamFree(probe);
                        }
                    }
                    catch { }
                    finally { pin.Free(); }

                    _bgmBytes[index] = data;
                    _bgmDurations[index] = Math.Max(totalSec, 0.0);
                    return;
                }

                long hash = ComputeFastHash(data);
                if (_contentHashToSample.TryGetValue(hash, out int existingSample))
                {
                    RegisterSample(index, existingSample);
                    return;
                }

                // Load directly into low-latency polyphonic sample (no blocking decode probe)
                int sample = Bass.SampleLoad(data, 0, data.Length, 32, BassFlags.SampleOverrideLongestPlaying);
                if (sample != 0)
                {
                    _contentHashToSample[hash] = sample;
                    RegisterSample(index, sample);
                }
                else
                {
                    Logger.Warn($"[AudioEngine] Failed to load sample from memory {index} ({name}): {Bass.LastError}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[AudioEngine] Error loading in-memory sound {index} ({name}): {ex.Message}");
            }
        }

        public void LoadArchiveSamples(string archivePath, Dictionary<int, byte[]> samples)
        {
            if (string.IsNullOrEmpty(archivePath) || samples == null || samples.Count == 0) return;

            DateTime lastWrite = File.Exists(archivePath) ? File.GetLastWriteTimeUtc(archivePath) : DateTime.MinValue;

            lock (_cachedArchives)
            {
                if (_cachedArchives.TryGetValue(archivePath, out var cached) && cached.LastWriteTime == lastWrite)
                {
                    _archiveLruOrder.Remove(archivePath);
                    _archiveLruOrder.Add(archivePath);
                    ActivateArchive(cached);
                    return;
                }
            }

            var newArchive = new ArchiveAudioCache
            {
                ArchivePath = archivePath,
                LastWriteTime = lastWrite
            };

            bool isSingleSample = (samples.Count == 1);
            newArchive.IsSingleSampleBgm = isSingleSample;

            if (isSingleSample)
            {
                foreach (var kvp in samples)
                {
                    int index = kvp.Key;
                    byte[] data = kvp.Value;
                    if (data == null || data.Length == 0) continue;

                    double totalSec = 0.0;
                    GCHandle pin = GCHandle.Alloc(data, GCHandleType.Pinned);
                    try
                    {
                        int probe = Bass.CreateStream(pin.AddrOfPinnedObject(), 0, data.Length, BassFlags.Decode);
                        if (probe != 0)
                        {
                            long lenBytes = Bass.ChannelGetLength(probe);
                            totalSec = Bass.ChannelBytes2Seconds(probe, lenBytes);
                            Bass.StreamFree(probe);
                        }
                    }
                    catch { }
                    finally { pin.Free(); }

                    newArchive.BgmBytes[index] = data;
                    newArchive.BgmDurations[index] = Math.Max(totalSec, 0.0);
                }
            }
            else
            {
                // Keysounded O2Jam: load all samples directly as polyphonic BASS samples (zero blocking decode probe)
                foreach (var kvp in samples)
                {
                    int index = kvp.Key;
                    byte[] data = kvp.Value;
                    if (data == null || data.Length == 0) continue;

                    long hash = ComputeFastHash(data);
                    if (newArchive.ContentHashToSample.TryGetValue(hash, out int existingHandle))
                    {
                        newArchive.SampleHandles[index] = existingHandle;
                        var info = new SampleInfo();
                        if (Bass.SampleGetInfo(existingHandle, info))
                        {
                            newArchive.SampleFrequencies[index] = (float)info.Frequency;
                        }
                        else
                        {
                            newArchive.SampleFrequencies[index] = 44100f;
                        }
                        continue;
                    }

                    int sampleHandle = Bass.SampleLoad(data, 0, data.Length, 32, BassFlags.SampleOverrideLongestPlaying);
                    if (sampleHandle != 0)
                    {
                        newArchive.ContentHashToSample[hash] = sampleHandle;
                        newArchive.SampleHandles[index] = sampleHandle;

                        var info = new SampleInfo();
                        if (Bass.SampleGetInfo(sampleHandle, info))
                        {
                            if ((info.Flags & BassFlags.Loop) != 0)
                            {
                                info.Flags &= ~BassFlags.Loop;
                                Bass.SampleSetInfo(sampleHandle, info);
                            }
                            newArchive.SampleFrequencies[index] = (float)info.Frequency;
                        }
                        else
                        {
                            newArchive.SampleFrequencies[index] = 44100f;
                        }
                    }
                    else
                    {
                        Logger.Warn($"[AudioEngine] Failed to load sample {index} from archive: {Bass.LastError}");
                    }
                }
            }

            lock (_cachedArchives)
            {
                while (_cachedArchives.Count >= MAX_CACHED_ARCHIVES && _archiveLruOrder.Count > 0)
                {
                    string oldestPath = _archiveLruOrder[0];
                    _archiveLruOrder.RemoveAt(0);
                    if (_cachedArchives.TryGetValue(oldestPath, out var evicted))
                    {
                        _cachedArchives.Remove(oldestPath);
                        FreeArchiveNativeHandles(evicted);
                    }
                }

                _cachedArchives[archivePath] = newArchive;
                _archiveLruOrder.Remove(archivePath);
                _archiveLruOrder.Add(archivePath);
            }

            ActivateArchive(newArchive);
        }

        private void ActivateArchive(ArchiveAudioCache archive)
        {
            StopAll();
            StopBgmInternal();

            FreeUncachedSampleHandles();

            _sampleHandles.Clear();
            _sampleFrequencies.Clear();
            _sampleDurationCache.Clear();
            _contentHashToSample.Clear();
            _bgmTracks.Clear();
            _bgmBytes.Clear();
            _bgmDurations.Clear();

            foreach (var kvp in archive.SampleHandles) _sampleHandles[kvp.Key] = kvp.Value;
            foreach (var kvp in archive.SampleFrequencies) _sampleFrequencies[kvp.Key] = kvp.Value;
            foreach (var kvp in archive.ContentHashToSample) _contentHashToSample[kvp.Key] = kvp.Value;
            foreach (var kvp in archive.BgmBytes) _bgmBytes[kvp.Key] = kvp.Value;
            foreach (var kvp in archive.BgmDurations) _bgmDurations[kvp.Key] = kvp.Value;

            CurrentArchiveSource = archive.ArchivePath;
            CurrentArchiveTime = archive.LastWriteTime;
            _isPaused = false;
            _bgmArmed = false;
            Bass.Start();
        }

        private static void FreeArchiveNativeHandles(ArchiveAudioCache archive)
        {
            var uniqueHandles = new HashSet<int>(archive.SampleHandles.Values);
            foreach (var handle in uniqueHandles)
            {
                if (handle != 0)
                {
                    Bass.SampleFree(handle);
                }
            }
            archive.SampleHandles.Clear();
            archive.ContentHashToSample.Clear();
        }

        private void FreeUncachedSampleHandles()
        {
            var cachedHandles = new HashSet<int>();
            lock (_cachedArchives)
            {
                foreach (var arch in _cachedArchives.Values)
                {
                    foreach (var h in arch.SampleHandles.Values)
                    {
                        cachedHandles.Add(h);
                    }
                }
            }

            var toFree = new HashSet<int>();
            foreach (var h in _sampleHandles.Values)
            {
                if (!cachedHandles.Contains(h))
                {
                    toFree.Add(h);
                }
            }

            foreach (var h in toFree)
            {
                Bass.SampleFree(h);
            }
        }

        public void PlayKeysound(int index, float volume = 1.0f, float pan = 0.0f)
        {
            if (_isPaused) return;
            float effVol = _isKeySoundEnabled ? volume : 0.0f;
            TriggerSample(index, effVol, pan, isBackground: false, baseVolume: volume);
        }

        public void PlayBackgroundKeysound(int index, float volume = 1.0f, float pan = 0.0f)
        {
            if (_isPaused) return;
            float effVol = _isBgmEnabled ? volume : 0.0f;
            TriggerSample(index, effVol, pan, isBackground: true, baseVolume: volume);
        }

        // Starts a background sample part-way through (used after a seek so long samples pick up where they
        // should be). While the device is paused the start is queued and performed by Resume().
        public void PlayBackgroundKeysoundAt(int index, double offsetSeconds, float volume = 1.0f, float pan = 0.0f)
        {
            if (!_sampleHandles.ContainsKey(index)) return;

            if (_isPaused)
            {
                _pendingSamples.Add((index, offsetSeconds, volume, pan));
                return;
            }

            StartBackgroundSample(index, offsetSeconds, volume, pan);
        }

        private void StartBackgroundSample(int index, double offsetSeconds, float volume, float pan)
        {
            float effVol = _isBgmEnabled ? volume : 0.0f;
            TriggerSample(index, effVol, pan, isBackground: true, baseVolume: volume, offsetSeconds: offsetSeconds);
        }

        // Plays one shot of a loaded sample on an available channel (optionally starting part-way through).
        private void TriggerSample(int index, float volume, float pan, bool isBackground, float baseVolume, double offsetSeconds = 0.0)
        {
            if (!_sampleHandles.TryGetValue(index, out int sampleHandle)) return;

            int ch = Bass.SampleGetChannel(sampleHandle);
            if (ch == 0) return;

            // Strip BassFlags.Loop so the channel plays as a strict non-looping one-shot
            Bass.ChannelFlags(ch, 0, BassFlags.Loop);

            float baseFreq = _sampleFrequencies.TryGetValue(index, out float f) ? f : 0f;
            if (baseFreq <= 0f)
            {
                var info = new SampleInfo();
                if (Bass.SampleGetInfo(sampleHandle, info))
                {
                    baseFreq = (float)info.Frequency;
                    _sampleFrequencies[index] = baseFreq;
                }
                else
                {
                    baseFreq = 44100f;
                }
            }
            float targetFreq = baseFreq * _musicSpeed;

            Bass.ChannelSetAttribute(ch, ChannelAttribute.Frequency, targetFreq);
            Bass.ChannelSetAttribute(ch, ChannelAttribute.Volume, Math.Clamp(volume, 0.0f, 1.0f));
            Bass.ChannelSetAttribute(ch, ChannelAttribute.Pan, Math.Clamp(pan, -1.0f, 1.0f));

            if (offsetSeconds > 0.0)
            {
                // Restart:true would rewind to 0, so position first and play without restart.
                long bytePos = Bass.ChannelSeconds2Bytes(ch, offsetSeconds);
                if (bytePos < 0 || !Bass.ChannelSetPosition(ch, bytePos))
                {
                    Bass.ChannelStop(ch); // offset is past the end of the sample
                    return;
                }
                Bass.ChannelPlay(ch, Restart: false);
            }
            else
            {
                Bass.ChannelPlay(ch, Restart: true);
            }

            lock (_activeChannels)
            {
                if (!_activeChannels.Contains(ch))
                {
                    _activeChannels.Add(ch);
                }
                if (isBackground)
                {
                    _activeKeyChannels.Remove(ch);
                    if (!_activeBgmChannels.Contains(ch)) _activeBgmChannels.Add(ch);
                }
                else
                {
                    _activeBgmChannels.Remove(ch);
                    if (!_activeKeyChannels.Contains(ch)) _activeKeyChannels.Add(ch);
                }
                _activeChannelFreqs[ch] = baseFreq;
                _activeChannelBaseVols[ch] = baseVolume;
            }
        }

        private bool _clockStall = false;

        // True if the last Resume()/PlayBgm() blocked long enough to inflate the next frame's dt.
        public bool ConsumeClockStall()
        {
            bool v = _clockStall;
            _clockStall = false;
            return v;
        }

        private void NoteStall(long startTimestamp)
        {
            double sec = (System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp) / (double)System.Diagnostics.Stopwatch.Frequency;
            if (sec > 0.03) _clockStall = true;
        }

        public void Resume()
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            ResumeCore();
            NoteStall(t0);
        }

        public void PlayBgm(int index, double offsetSeconds, bool play)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            PlayBgmCore(index, offsetSeconds, play);
            NoteStall(t0);
        }

        private void PlayBgmCore(int index, double offsetSeconds, bool play)
        {
            // Never touch the stream while the device is paused (pause/seek on a device-paused channel is
            // unreliable and left the old position playing). Remember the request; Resume() applies it.
            if (_isPaused)
            {
                _pendingBgm = (index, offsetSeconds, play);
                return;
            }

            bool isFile = _bgmTracks.TryGetValue(index, out string? filePath);
            bool isBytes = _bgmBytes.TryGetValue(index, out byte[]? byteData);
            if (!isFile && !isBytes) return;

            try
            {
                bool trackChanged = (_currentBgmIndex != index || _bgmStream == 0);
                if (trackChanged)
                {
                    StopBgmInternal();

                    _currentBgmIndex = index;
                    if (isFile && filePath != null)
                    {
                        _bgmStream = Bass.CreateStream(filePath, 0, 0, BassFlags.Prescan);
                        if (_bgmStream == 0)
                        {
                            _bgmStream = Bass.CreateStream(filePath, 0, 0, BassFlags.Default);
                        }
                    }
                    else if (isBytes && byteData != null)
                    {
                        if (_pinnedBgmHandle.IsAllocated)
                        {
                            _pinnedBgmHandle.Free();
                        }
                        _pinnedBgmHandle = GCHandle.Alloc(byteData, GCHandleType.Pinned);
                        _bgmStream = Bass.CreateStream(_pinnedBgmHandle.AddrOfPinnedObject(), 0, byteData.Length, BassFlags.Prescan);
                        if (_bgmStream == 0)
                        {
                            _bgmStream = Bass.CreateStream(_pinnedBgmHandle.AddrOfPinnedObject(), 0, byteData.Length, BassFlags.Default);
                        }
                    }

                    if (_bgmStream != 0)
                    {
                        Bass.ChannelFlags(_bgmStream, 0, BassFlags.Loop);
                        if (Bass.ChannelGetAttribute(_bgmStream, ChannelAttribute.Frequency, out float bFreq) && bFreq > 0f)
                        {
                            _bgmBaseFreq = bFreq;
                        }
                        else
                        {
                            _bgmBaseFreq = 44100f;
                        }
                        Bass.ChannelSetAttribute(_bgmStream, ChannelAttribute.Frequency, _bgmBaseFreq * _musicSpeed);
                        Bass.ChannelSetAttribute(_bgmStream, ChannelAttribute.Volume, _isBgmEnabled ? 1.0f : 0.0f);
                    }
                }

                if (_bgmStream != 0)
                {
                    long totalBytes = Bass.ChannelGetLength(_bgmStream);
                    double totalSec = Bass.ChannelBytes2Seconds(_bgmStream, totalBytes);
                    double targetSec = Math.Clamp(offsetSeconds, 0, totalSec);

                    if (targetSec < totalSec - 0.05)
                    {
                        long curPosBytes = Bass.ChannelGetPosition(_bgmStream);
                        double curSec = Bass.ChannelBytes2Seconds(_bgmStream, curPosBytes);
                        if (!trackChanged && Math.Abs(curSec - targetSec) < 0.15 && Bass.ChannelIsActive(_bgmStream) == PlaybackState.Playing)
                        {
                            // Already actively playing at the right position; do not re-seek or restart
                            return;
                        }

                        long targetByte = Bass.ChannelSeconds2Bytes(_bgmStream, targetSec);
                        if (!Bass.ChannelSetPosition(_bgmStream, targetByte))
                        {
                            Logger.Warn($"[AudioEngine] BGM {index}: seek to {targetSec:F2}s failed: {Bass.LastError}");
                        }
                        Bass.ChannelUpdate(_bgmStream, 0);
                    }
                    else
                    {
                        Bass.ChannelPause(_bgmStream);
                        _bgmArmed = false;
                        return;
                    }

                    Bass.ChannelSetAttribute(_bgmStream, ChannelAttribute.Volume, _isBgmEnabled ? 1.0f : 0.0f);

                    // "play" = the BGM should be audible at this position. If the device is paused we only
                    // arm it; Resume() starts it from exactly this position.
                    _bgmArmed = play;
                    if (play && !_isPaused)
                    {
                        if (Bass.ChannelIsActive(_bgmStream) != PlaybackState.Playing)
                        {
                            Bass.ChannelPlay(_bgmStream, false);
                        }
                    }
                    else if (Bass.ChannelIsActive(_bgmStream) == PlaybackState.Playing)
                    {
                        Bass.ChannelPause(_bgmStream);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[AudioEngine] Error playing BGM {index}: {ex.Message}");
            }
        }

        public void PauseBgm()
        {
            _bgmArmed = false;
            _pendingBgm = null;
            if (_bgmStream != 0 && Bass.ChannelIsActive(_bgmStream) == PlaybackState.Playing)
            {
                Bass.ChannelPause(_bgmStream);
            }
        }

        public void Pause()
        {
            _isPaused = true;
            // Freezes entire BASS output device instantly so all keysounds and BGM hold position
            Bass.Pause();
        }

        private void ResumeCore()
        {
            _isPaused = false;
            // Resumes output device so all sounds continue in sync
            Bass.Start();

            // Long background samples that were re-positioned by a seek while paused
            if (_pendingSamples.Count > 0)
            {
                foreach (var p in _pendingSamples)
                {
                    StartBackgroundSample(p.Index, p.Offset, p.Volume, p.Pan);
                }
                _pendingSamples.Clear();
            }

            // Only restart BGM that is supposed to be sounding at this song position. Blindly playing the
            // stream here made BGM start early when paused before its first trigger note.
            if (_pendingBgm is { } pb)
            {
                _pendingBgm = null;
                PlayBgm(pb.Index, pb.Offset, pb.Play);
            }
            else if (_bgmArmed && _bgmStream != 0 && Bass.ChannelIsActive(_bgmStream) != PlaybackState.Playing)
            {
                Bass.ChannelPlay(_bgmStream, false);
            }
        }

        public void StopAll()
        {
            // Silences all currently ringing keysound channels (e.g. during seek)
            lock (_activeChannels)
            {
                foreach (int ch in _activeChannels)
                {
                    if (Bass.ChannelIsActive(ch) != PlaybackState.Stopped)
                    {
                        Bass.ChannelStop(ch);
                    }
                }
                _activeChannels.Clear();
                _activeBgmChannels.Clear();
                _activeKeyChannels.Clear();
                _activeChannelFreqs.Clear();
                _activeChannelBaseVols.Clear();
            }

            _pendingSamples.Clear();
            _bgmArmed = false;
            _pendingBgm = null;

            if (_bgmStream != 0)
            {
                if (_isPaused)
                {
                    // Device is paused: drop the stream so the old position can't come back on Resume().
                    // PlayBgm() recreates it at the new position.
                    StopBgmInternal();
                }
                else
                {
                    Bass.ChannelPause(_bgmStream);
                }
            }
        }

        private double _lastChannelPruneSeconds = 0.0;

        public void Update()
        {
            // Prune finished channels every 250ms (or if channel list exceeds 48)
            // This eliminates 95%+ of per-frame BASS native interop calls during rendering.
            double now = DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond;
            if (now - _lastChannelPruneSeconds < 0.25 && _activeChannels.Count < 48)
            {
                return;
            }
            _lastChannelPruneSeconds = now;

            lock (_activeChannels)
            {
                for (int i = _activeChannels.Count - 1; i >= 0; i--)
                {
                    int ch = _activeChannels[i];
                    if (Bass.ChannelIsActive(ch) == PlaybackState.Stopped)
                    {
                        _activeChannels.RemoveAt(i);
                        _activeBgmChannels.Remove(ch);
                        _activeKeyChannels.Remove(ch);
                        _activeChannelFreqs.Remove(ch);
                        _activeChannelBaseVols.Remove(ch);
                    }
                }
            }
        }

        public void StopBgm()
        {
            StopBgmInternal();
        }

        private void StopBgmInternal()
        {
            try
            {
                if (_bgmStream != 0)
                {
                    Bass.ChannelStop(_bgmStream);
                    Bass.StreamFree(_bgmStream);
                    _bgmStream = 0;
                }
                if (_pinnedBgmHandle.IsAllocated)
                {
                    _pinnedBgmHandle.Free();
                }
                _currentBgmIndex = -1;
                _bgmArmed = false;
                _pendingBgm = null;
            }
            catch (Exception ex)
            {
                Logger.Error($"[AudioEngine] Error stopping BGM: {ex.Message}");
            }
        }

        public void Clear()
        {
            StopAll();
            StopBgmInternal();
            _bgmTracks.Clear();
            _bgmBytes.Clear();
            _bgmDurations.Clear();

            FreeUncachedSampleHandles();

            _sampleHandles.Clear();
            _sampleFrequencies.Clear();
            _sampleDurationCache.Clear();
            _contentHashToSample.Clear();

            CurrentArchiveSource = string.Empty;
            CurrentArchiveTime = default;
            _isPaused = false;
            _bgmArmed = false;
            Bass.Start();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Clear();
                lock (_cachedArchives)
                {
                    foreach (var arch in _cachedArchives.Values)
                    {
                        FreeArchiveNativeHandles(arch);
                    }
                    _cachedArchives.Clear();
                    _archiveLruOrder.Clear();
                }
                if (_pinnedBgmHandle.IsAllocated)
                {
                    _pinnedBgmHandle.Free();
                }
                try
                {
                    Bass.Free();
                }
                catch { }
                GC.SuppressFinalize(this);
            }
        }

        ~AudioEngine()
        {
            Dispose();
        }
    }
}
