using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace O2Play
{
    public class BmsHeader
    {
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Genre { get; set; } = string.Empty;
        public string Noter { get; set; } = string.Empty;
        public double InitialBpm { get; set; } = 130.0;
        public int PlayLevel { get; set; } = 1;
        public int LnObj { get; set; } = 0; // #LNOBJ xx

        // Maps base36 integer index to WAV filename
        public Dictionary<int, string> Wavs { get; set; } = new Dictionary<int, string>();
        
        // Maps base36 integer index to BPM change value (#BPMxx)
        public Dictionary<int, double> Bpms { get; set; } = new Dictionary<int, double>();
    }

    public class BmsMeasure
    {
        public int Index { get; set; }
        public double LengthFactor { get; set; } = 1.0;
        public double StartTick { get; set; }
        public double EndTick { get; set; }
        public double Ticks => EndTick - StartTick;
        public double StartTimeSeconds { get; set; }
        public double DurationSeconds { get; set; }
        public double EndTimeSeconds => StartTimeSeconds + DurationSeconds;
    }

    public class BmsBpmChange
    {
        public double Tick { get; set; }
        public double TimeSeconds { get; set; }
        public double Bpm { get; set; }
    }

    public class BmsNote
    {
        public int Measure { get; set; }
        public int Channel { get; set; }
        public double Position { get; set; } // 0.0 to 1.0 fraction of the measure
        public int Value { get; set; } // The base36 value of the note/WAV
        public double Tick { get; set; }
        public double DurationTicks { get; set; } = 0.0;
        public double TimeSeconds { get; set; }
        public double DurationSeconds { get; set; } = 0.0;
        public bool IsHit { get; set; }
        public bool IsHolding { get; set; }
        
        public int Lane { get; set; } = 0; // 1-7 for playable keys, 0 for BGM/keysound
        public bool IsKeysound => Lane == 0;
        public int SoundIndex => Value;
        public bool IsLongNote => DurationTicks > 0.0 || DurationSeconds > 0.0;

        public float Volume { get; set; } = 1.0f;
        public float Pan { get; set; } = 0.0f;
    }

    public class BmsChart
    {
        public string FilePath { get; set; } = string.Empty;
        public bool IsDummy => Notes.Count == 0;
        public BmsHeader Header { get; set; } = new BmsHeader();
        public List<BmsMeasure> Measures { get; set; } = new List<BmsMeasure>();
        public Dictionary<int, double> MeasureFactors { get; set; } = new Dictionary<int, double>();
        public List<BmsBpmChange> BpmChanges { get; set; } = new List<BmsBpmChange>();
        public List<BmsNote> Notes { get; set; } = new List<BmsNote>();
        public OjnDifficulty? CurrentDifficulty { get; set; }
        public bool[] AvailableDifficulties { get; set; } = new bool[3];
        public int[] DifficultyLevels { get; set; } = new int[3];
        public int[] DifficultyNoteCounts { get; set; } = new int[3];
        public bool IsOjn => CurrentDifficulty.HasValue;

        private int? _playableNoteCount;
        // Total count of playable notes on lanes 1..7, excluding lane 0 keysounds.
        public int PlayableNoteCount
        {
            get
            {
                if (!_playableNoteCount.HasValue)
                {
                    int count = 0;
                    for (int i = 0; i < Notes.Count; i++)
                    {
                        if (!Notes[i].IsKeysound) count++;
                    }
                    _playableNoteCount = count;
                }
                return _playableNoteCount.Value;
            }
        }

        private int? _totalNoteCount;
        // Total note hits including long note tails (Tap = 1 hit, Long note = 2 hits).
        public int TotalNoteCount
        {
            get
            {
                if (!_totalNoteCount.HasValue)
                {
                    if (IsOjn && CurrentDifficulty.HasValue)
                    {
                        int diffIdx = (int)CurrentDifficulty.Value;
                        if (diffIdx >= 0 && diffIdx < DifficultyNoteCounts.Length && DifficultyNoteCounts[diffIdx] > 0)
                        {
                            _totalNoteCount = DifficultyNoteCounts[diffIdx];
                            return _totalNoteCount.Value;
                        }
                    }
                    int total = 0;
                    for (int i = 0; i < Notes.Count; i++)
                    {
                        var n = Notes[i];
                        if (!n.IsKeysound)
                        {
                            total += n.IsLongNote ? 2 : 1;
                        }
                    }
                    _totalNoteCount = total;
                }
                return _totalNoteCount.Value;
            }
        }

        public void InvalidateNoteCounts()
        {
            _playableNoteCount = null;
            _totalNoteCount = null;
        }

        public static double SanitizeBpm(double bpm)
        {
            // Handle BPM overflow at int32_t range (matching TimingBase::GetBPMAt)
            if (bpm >= int.MaxValue || bpm <= -int.MaxValue)
            {
                bpm = bpm % int.MaxValue;
            }

            return bpm;
        }

        public double GetBpmAt(double offset)
        {
            double bpm = Header.InitialBpm;
            if (BpmChanges.Count > 0)
            {
                for (int i = BpmChanges.Count - 1; i >= 0; i--)
                {
                    if (offset >= BpmChanges[i].TimeSeconds)
                    {
                        bpm = BpmChanges[i].Bpm;
                        break;
                    }
                }
            }

            // Handle BPM overflow at int32_t range
            if (bpm >= int.MaxValue || bpm <= -int.MaxValue)
            {
                bpm = bpm % int.MaxValue;
            }

            return bpm;
        }

        public double GetBpmAtTick(double tick)
        {
            double bpm = Header.InitialBpm;
            if (BpmChanges.Count > 0)
            {
                for (int i = BpmChanges.Count - 1; i >= 0; i--)
                {
                    if (tick >= BpmChanges[i].Tick)
                    {
                        bpm = BpmChanges[i].Bpm;
                        break;
                    }
                }
            }

            // Handle BPM overflow at int32_t range
            if (bpm >= int.MaxValue || bpm <= -int.MaxValue)
            {
                bpm = bpm % int.MaxValue;
            }

            return bpm;
        }

        private double? _cachedCommonBpm;

        // Calculates the duration-weighted base BPM across the chart.
        public double GetCommonBpm()
        {
            if (_cachedCommonBpm.HasValue) return _cachedCommonBpm.Value;

            if (BpmChanges.Count == 0)
            {
                double initBpm = SanitizeBpm(Header.InitialBpm > 0 ? Header.InitialBpm : 140.0);
                _cachedCommonBpm = (initBpm > 10.0 && !double.IsNaN(initBpm) && !double.IsInfinity(initBpm)) ? initBpm : 140.0;
                return _cachedCommonBpm.Value;
            }

            if (Notes.Count == 0)
            {
                double initBpm = SanitizeBpm(Header.InitialBpm > 0 ? Header.InitialBpm : 140.0);
                _cachedCommonBpm = (initBpm > 10.0 && !double.IsNaN(initBpm) && !double.IsInfinity(initBpm)) ? initBpm : 140.0;
                return _cachedCommonBpm.Value;
            }

            // Find the end time of the last note (matches O2Game: orderedByDescending(m_notes) -> lastObject)
            double lastTime = 0.0;
            foreach (var note in Notes)
            {
                double end = note.TimeSeconds + note.DurationSeconds;
                if (end > lastTime) lastTime = end;
            }

            if (lastTime <= 0)
            {
                double initBpm = SanitizeBpm(Header.InitialBpm > 0 ? Header.InitialBpm : 140.0);
                _cachedCommonBpm = (initBpm > 10.0 && !double.IsNaN(initBpm) && !double.IsInfinity(initBpm)) ? initBpm : 140.0;
                return _cachedCommonBpm.Value;
            }

            var durations = new Dictionary<double, double>();
            for (int i = BpmChanges.Count - 1; i >= 0; i--)
            {
                var tp = BpmChanges[i];
                if (tp.TimeSeconds > lastTime) continue;

                double duration = lastTime - (i == 0 ? 0.0 : tp.TimeSeconds);
                lastTime = tp.TimeSeconds;

                double bpm = SanitizeBpm(tp.Bpm);
                if (bpm > 10.0 && !double.IsNaN(bpm) && !double.IsInfinity(bpm))
                {
                    if (durations.ContainsKey(bpm))
                        durations[bpm] += duration;
                    else
                        durations[bpm] = duration;
                }
            }

            if (lastTime > 0)
            {
                double initBpm = SanitizeBpm(Header.InitialBpm > 0 ? Header.InitialBpm : 140.0);
                if (initBpm > 10.0 && !double.IsNaN(initBpm) && !double.IsInfinity(initBpm))
                {
                    if (durations.ContainsKey(initBpm))
                        durations[initBpm] += lastTime;
                    else
                        durations[initBpm] = lastTime;
                }
            }

            if (durations.Count == 0)
            {
                double initBpm = SanitizeBpm(Header.InitialBpm > 0 ? Header.InitialBpm : 140.0);
                _cachedCommonBpm = (initBpm > 10.0 && !double.IsNaN(initBpm) && !double.IsInfinity(initBpm)) ? initBpm : 140.0;
                return _cachedCommonBpm.Value;
            }

            double maxDuration = 0.0;
            double commonBpm = SanitizeBpm(Header.InitialBpm > 0 ? Header.InitialBpm : 140.0);

            foreach (var kvp in durations)
            {
                if (kvp.Value > maxDuration)
                {
                    maxDuration = kvp.Value;
                    commonBpm = kvp.Key;
                }
            }

            if (commonBpm <= 10.0 || double.IsNaN(commonBpm) || double.IsInfinity(commonBpm))
                commonBpm = 140.0;

            _cachedCommonBpm = commonBpm;
            return _cachedCommonBpm.Value;
        }

        public double TickToSeconds(double tick)
        {
            if (BpmChanges.Count == 0)
            {
                double bpm = SanitizeBpm(Header.InitialBpm);
                double ticksPerSec = (bpm * 48.0) / 60.0;
                return tick / ticksPerSec;
            }

            int seg = 0;
            for (int i = BpmChanges.Count - 1; i >= 0; i--)
            {
                if (tick >= BpmChanges[i].Tick)
                {
                    seg = i;
                    break;
                }
            }
            double deltaTicks = tick - BpmChanges[seg].Tick;
            double segTicksPerSec = (SanitizeBpm(BpmChanges[seg].Bpm) * 48.0) / 60.0;
            return BpmChanges[seg].TimeSeconds + (deltaTicks / segTicksPerSec);
        }

        public double SecondsToTick(double time)
        {
            if (BpmChanges.Count == 0)
            {
                double bpm = SanitizeBpm(Header.InitialBpm);
                double ticksPerSec = (bpm * 48.0) / 60.0;
                return time * ticksPerSec;
            }

            int seg = 0;
            for (int i = BpmChanges.Count - 1; i >= 0; i--)
            {
                if (time >= BpmChanges[i].TimeSeconds)
                {
                    seg = i;
                    break;
                }
            }
            double deltaTime = time - BpmChanges[seg].TimeSeconds;
            double segTicksPerSec = (SanitizeBpm(BpmChanges[seg].Bpm) * 48.0) / 60.0;
            return BpmChanges[seg].Tick + (deltaTime * segTicksPerSec);
        }

        public double SecondsToBeat(double time)
        {
            return SecondsToTick(time) / 48.0;
        }

        public int SecondsToMeasure(double time)
        {
            if (Measures.Count == 0) return 0;
            for (int i = Measures.Count - 1; i >= 0; i--)
            {
                if (time >= Measures[i].StartTimeSeconds - 1e-4)
                    return Measures[i].Index;
            }
            return Measures[0].Index;
        }
    }

    public static class BmsParser
    {
        public static BmsChart Parse(string filePath)
        {
            var chart = new BmsChart();
            chart.FilePath = filePath;
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"BMS file not found: {filePath}");

            byte[] fileBytes = File.ReadAllBytes(filePath);
            string[] lines;
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            // Check for UTF-8 BOM
            if (fileBytes.Length >= 3 && fileBytes[0] == 0xEF && fileBytes[1] == 0xBB && fileBytes[2] == 0xBF)
            {
                lines = File.ReadAllLines(filePath, System.Text.Encoding.UTF8);
            }
            else
            {
                // Check if file is valid UTF-8 with non-ASCII content
                bool isValidUtf8 = false;
                string? utf8Text = null;
                try
                {
                    var utf8Strict = new System.Text.UTF8Encoding(false, true);
                    utf8Text = utf8Strict.GetString(fileBytes);
                    bool hasMultiByte = false;
                    for (int i = 0; i < fileBytes.Length; i++)
                    {
                        if (fileBytes[i] >= 0x80) { hasMultiByte = true; break; }
                    }
                    if (hasMultiByte) isValidUtf8 = true;
                }
                catch
                {
                    isValidUtf8 = false;
                }

                if (isValidUtf8 && utf8Text != null)
                {
                    lines = utf8Text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                }
                else
                {
                    // Fall back to Shift-JIS (cp932)
                    try
                    {
                        lines = File.ReadAllLines(filePath, System.Text.Encoding.GetEncoding(932));
                    }
                    catch
                    {
                        lines = File.ReadAllLines(filePath);
                    }
                }
            }

            var rawBpmEvents = new List<(int measure, double position, double bpm)>();

            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || !trimmed.StartsWith("#") || trimmed.Length < 6)
                    continue;

                // Check if this is a Channel Command: #MMMCC:data or #MMMCC data where MMM is 3 digits
                if (char.IsDigit(trimmed[1]) && char.IsDigit(trimmed[2]) && char.IsDigit(trimmed[3]))
                {
                    string command = trimmed.Substring(1, 5).ToUpper();
                    string data = trimmed.Substring(6).TrimStart(':', ' ', '\t').Trim();
                    ParseChannelCommand(chart, rawBpmEvents, command, data);
                }
                else
                {
                    // Header command (e.g. #TITLE Song Name or #WAV01 kick.wav)
                    int sep = trimmed.IndexOfAny(new[] { ' ', ':', '\t' });
                    if (sep != -1)
                    {
                        string command = trimmed.Substring(1, sep - 1).ToUpper();
                        string value = trimmed.Substring(sep + 1).Trim();
                        ParseHeaderCommand(chart.Header, command, value);
                    }
                }
            }

            CalculateTimings(chart, rawBpmEvents);
            return chart;
        }

        public static void CalculateTimings(BmsChart chart, List<(int measure, double position, double bpm)>? rawBpmEvents = null)
        {
            int maxMeasure = 0;
            foreach (var note in chart.Notes)
            {
                if (note.Measure > maxMeasure) maxMeasure = note.Measure;
            }
            foreach (var mIdx in chart.MeasureFactors.Keys)
            {
                if (mIdx > maxMeasure) maxMeasure = mIdx;
            }
            if (rawBpmEvents != null)
            {
                foreach (var b in rawBpmEvents)
                {
                    if (b.measure > maxMeasure) maxMeasure = b.measure;
                }
            }

            // 192 ticks per standard 4/4 measure
            chart.Measures.Clear();
            double curTick = 0.0;
            for (int m = 0; m <= maxMeasure + 2; m++)
            {
                double factor = chart.MeasureFactors.TryGetValue(m, out double f) && f > 0 ? f : 1.0;
                double measureTicks = 192.0 * factor;

                var bmsM = new BmsMeasure
                {
                    Index = m,
                    LengthFactor = factor,
                    StartTick = curTick,
                    EndTick = curTick + measureTicks
                };
                chart.Measures.Add(bmsM);
                curTick = bmsM.EndTick;
            }

            foreach (var note in chart.Notes)
            {
                int mIdx = Math.Clamp(note.Measure, 0, chart.Measures.Count - 1);
                var m = chart.Measures[mIdx];
                note.Tick = m.StartTick + (note.Position * m.Ticks);
            }

            chart.BpmChanges.Clear();
            double initBpm = chart.Header.InitialBpm > 0 ? chart.Header.InitialBpm : 130.0;
            chart.Header.InitialBpm = initBpm;

            var allBpmEvents = new List<BmsBpmChange>();
            allBpmEvents.Add(new BmsBpmChange { Tick = 0.0, Bpm = initBpm });

            if (rawBpmEvents != null)
            {
                foreach (var ev in rawBpmEvents)
                {
                    int mIdx = Math.Clamp(ev.measure, 0, chart.Measures.Count - 1);
                    var m = chart.Measures[mIdx];
                    double tick = m.StartTick + (ev.position * m.Ticks);
                    if (ev.bpm > 0)
                    {
                        allBpmEvents.Add(new BmsBpmChange { Tick = tick, Bpm = ev.bpm });
                    }
                }
            }

            allBpmEvents.Sort((a, b) => a.Tick.CompareTo(b.Tick));

            for (int i = 0; i < allBpmEvents.Count; i++)
            {
                if (chart.BpmChanges.Count == 0 || Math.Abs(allBpmEvents[i].Tick - chart.BpmChanges[^1].Tick) > 0.001)
                {
                    chart.BpmChanges.Add(allBpmEvents[i]);
                }
                else
                {
                    chart.BpmChanges[^1].Bpm = allBpmEvents[i].Bpm;
                }
            }

            chart.BpmChanges[0].TimeSeconds = 0.0;
            for (int i = 1; i < chart.BpmChanges.Count; i++)
            {
                double deltaTicks = chart.BpmChanges[i].Tick - chart.BpmChanges[i - 1].Tick;
                double segTicksPerSec = (chart.BpmChanges[i - 1].Bpm * 48.0) / 60.0;
                chart.BpmChanges[i].TimeSeconds = chart.BpmChanges[i - 1].TimeSeconds + (deltaTicks / segTicksPerSec);
            }

            foreach (var m in chart.Measures)
            {
                m.StartTimeSeconds = chart.TickToSeconds(m.StartTick);
                m.DurationSeconds = chart.TickToSeconds(m.EndTick) - m.StartTimeSeconds;
            }

            foreach (var note in chart.Notes)
            {
                note.TimeSeconds = chart.TickToSeconds(note.Tick);
            }

            chart.Notes.Sort((a, b) => a.Tick.CompareTo(b.Tick));

            var presentChannels = new HashSet<int>();
            foreach (var n in chart.Notes)
            {
                if ((n.Channel >= 11 && n.Channel <= 19) || (n.Channel >= 51 && n.Channel <= 59))
                {
                    int baseChan = n.Channel >= 50 ? n.Channel - 40 : n.Channel;
                    presentChannels.Add(baseChan);
                }
            }

            bool hasCh19 = presentChannels.Contains(19);
            bool hasCh18 = presentChannels.Contains(18);
            bool hasCh17 = presentChannels.Contains(17);
            bool hasCh16 = presentChannels.Contains(16);

            foreach (var n in chart.Notes)
            {
                if (n.Channel > 0)
                {
                    n.Lane = ResolveLane(n.Channel, hasCh19, hasCh18, hasCh17, hasCh16);
                }
            }

            // Pair Long Notes
            // Pattern A: Channels 51..59 pairing
            for (int lane = 1; lane <= 7; lane++)
            {
                var laneLNs = chart.Notes.FindAll(n => n.Lane == lane && IsLongChannel(n.Channel));
                laneLNs.Sort((a, b) => a.Tick.CompareTo(b.Tick));
                for (int i = 0; i < laneLNs.Count - 1; i += 2)
                {
                    var startNote = laneLNs[i];
                    var endNote = laneLNs[i + 1];
                    startNote.DurationTicks = Math.Max(1.0, endNote.Tick - startNote.Tick);
                    startNote.DurationSeconds = Math.Max(0.05, endNote.TimeSeconds - startNote.TimeSeconds);
                    endNote.IsHit = true;
                }
            }

            // Pattern B: #LNOBJ xx pairing on normal note channels
            if (chart.Header.LnObj > 0)
            {
                for (int lane = 1; lane <= 7; lane++)
                {
                    var laneNotes = chart.Notes.FindAll(n => n.Lane == lane && !IsLongChannel(n.Channel));
                    laneNotes.Sort((a, b) => a.Tick.CompareTo(b.Tick));
                    for (int i = 0; i < laneNotes.Count; i++)
                    {
                        if (laneNotes[i].Value == chart.Header.LnObj && i > 0)
                        {
                            var prev = laneNotes[i - 1];
                            if (!prev.IsLongNote && !prev.IsHit)
                            {
                                prev.DurationTicks = Math.Max(1.0, laneNotes[i].Tick - prev.Tick);
                                prev.DurationSeconds = Math.Max(0.05, laneNotes[i].TimeSeconds - prev.TimeSeconds);
                                laneNotes[i].IsHit = true;
                            }
                        }
                    }
                }
            }
        }

        private static bool IsLongChannel(int channel)
        {
            return channel >= 51 && channel <= 59;
        }

        private static int ResolveLane(int channel, bool hasCh19, bool hasCh18, bool hasCh17, bool hasCh16)
        {
            if (channel == 1) return 0; // Channel 01: BGM

            // Normalize long note channel 51..59 to 11..19
            int baseChan = (channel >= 51 && channel <= 59) ? channel - 40 : channel;

            if (hasCh16)
            {
                // Map BMS channels 16, 11-15, 18/19 to O2Jam 7-key lanes 1..7.
                if (baseChan == 16) return 1;
                if (baseChan == 11) return 2;
                if (baseChan == 12) return 3;
                if (baseChan == 13) return 4;
                if (baseChan == 14) return 5;
                if (baseChan == 15) return 6;
                if (baseChan == 18 || baseChan == 19) return 7;
            }
            else if (hasCh19 && hasCh18)
            {
                // Standard Beatmania IIDX 7-key (no scratch channel): 11..15 -> 1..5, 18 -> 6, 19 -> 7
                if (baseChan == 11) return 1;
                if (baseChan == 12) return 2;
                if (baseChan == 13) return 3;
                if (baseChan == 14) return 4;
                if (baseChan == 15) return 5;
                if (baseChan == 18) return 6;
                if (baseChan == 19) return 7;
            }
            else if (hasCh17)
            {
                // 11..17 -> Lanes 1..7 (Sequential style)
                if (baseChan >= 11 && baseChan <= 17) return baseChan - 10;
            }
            else
            {
                // 5-Key BMS: 11..15 -> Lanes 1..5
                if (baseChan >= 11 && baseChan <= 15) return baseChan - 10;
                if (baseChan == 16) return 1; // Scratch
            }

            return 0;
        }

        private static void ParseHeaderCommand(BmsHeader header, string command, string value)
        {
            if (command == "TITLE") header.Title = value;
            else if (command == "ARTIST") header.Artist = value;
            else if (command == "SUBARTIST" || command == "NOTER") header.Noter = value;
            else if (command == "GENRE") header.Genre = value;
            else if (command == "BPM" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double bpm)) header.InitialBpm = bpm;
            else if (command == "PLAYLEVEL" && int.TryParse(value, out int pl)) header.PlayLevel = pl;
            else if (command == "LNOBJ")
            {
                header.LnObj = Base36Decode(value.Trim());
            }
            else if (command.StartsWith("WAV"))
            {
                string idStr = command.Substring(3);
                int id = Base36Decode(idStr);
                header.Wavs[id] = value;
            }
            else if (command.StartsWith("BPM") && command.Length >= 4) // #BPMxx
            {
                string idStr = command.Substring(3);
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double bpmChange))
                {
                    int id = Base36Decode(idStr);
                    header.Bpms[id] = bpmChange;
                }
            }
        }

        private static void ParseChannelCommand(BmsChart chart, List<(int measure, double position, double bpm)> rawBpmEvents, string command, string data)
        {
            if (command.Length != 5) return;

            string measureStr = command.Substring(0, 3);
            string channelStr = command.Substring(3, 2);

            if (!int.TryParse(measureStr, out int measure)) return;
            if (!int.TryParse(channelStr, out int channel)) return;

            // Channel 02: Measure length multiplier (floating point number)
            if (channel == 2)
            {
                if (double.TryParse(data, NumberStyles.Float, CultureInfo.InvariantCulture, out double factor))
                {
                    chart.MeasureFactors[measure] = factor;
                }
                return;
            }

            data = data.Replace(" ", "").Replace("\t", "");

            int len = data.Length / 2;
            if (len == 0) return;

            // Channel 03: Hexadecimal BPM change
            if (channel == 3)
            {
                for (int i = 0; i < len; i++)
                {
                    string valStr = data.Substring(i * 2, 2);
                    if (valStr == "00") continue;
                    try
                    {
                        int bpmVal = Convert.ToInt32(valStr, 16);
                        if (bpmVal > 0)
                        {
                            rawBpmEvents.Add((measure, (double)i / len, bpmVal));
                        }
                    }
                    catch { }
                }
                return;
            }

            // Channel 08: Extended BPM change (references #BPMxx table)
            if (channel == 8)
            {
                for (int i = 0; i < len; i++)
                {
                    string valStr = data.Substring(i * 2, 2);
                    if (valStr == "00") continue;
                    int bpmId = Base36Decode(valStr);
                    if (chart.Header.Bpms.TryGetValue(bpmId, out double extBpm))
                    {
                        rawBpmEvents.Add((measure, (double)i / len, extBpm));
                    }
                }
                return;
            }

            // Standard Note Channels (01: BGM, 11..19: Playable keys, 51..59: Long notes)
            for (int i = 0; i < len; i++)
            {
                string valStr = data.Substring(i * 2, 2);
                if (valStr == "00") continue;

                int value = Base36Decode(valStr);
                chart.Notes.Add(new BmsNote
                {
                    Measure = measure,
                    Channel = channel,
                    Position = (double)i / len,
                    Value = value
                });
            }
        }

        public static int Base36Decode(string input)
        {
            input = input.Trim().ToUpper();
            int result = 0;
            foreach (char c in input)
            {
                result *= 36;
                if (c >= '0' && c <= '9')
                    result += (c - '0');
                else if (c >= 'A' && c <= 'Z')
                    result += (c - 'A' + 10);
            }
            return result;
        }
    }
}
