using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace O2Play
{
    public enum OjnDifficulty
    {
        EX = 0, // Easy
        NX = 1, // Normal
        HX = 2  // Hard
    }

    public static class OjnParser
    {
        // Registered once for the process instead of on every DecodeString call.
        static OjnParser()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public static BmsChart Parse(string filePath, AudioEngine? audio = null, OjnDifficulty? preferredDifficulty = null)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"OJN file not found: {filePath}");

            byte[] rawBytes = File.ReadAllBytes(filePath);
            byte[] data = Decrypt(rawBytes);

            if (data.Length < 300)
                throw new InvalidDataException("OJN file is too small to contain a valid header.");

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            // Read 300-byte O2Jam chart header
            int songId = reader.ReadInt32();
            byte[] signature = reader.ReadBytes(4);
            float encodingVersion = reader.ReadSingle();
            int genreInt = reader.ReadInt32();
            float bpm = reader.ReadSingle();
            short levelEx = reader.ReadInt16();
            short levelNx = reader.ReadInt16();
            short levelHx = reader.ReadInt16();
            short unk1 = reader.ReadInt16();
            int eventCountEx = reader.ReadInt32();
            int eventCountNx = reader.ReadInt32();
            int eventCountHx = reader.ReadInt32();
            int noteCountEx = reader.ReadInt32();
            int noteCountNx = reader.ReadInt32();
            int noteCountHx = reader.ReadInt32();
            int measureCountEx = reader.ReadInt32();
            int measureCountNx = reader.ReadInt32();
            int measureCountHx = reader.ReadInt32();
            int blockCountEx = reader.ReadInt32();
            int blockCountNx = reader.ReadInt32();
            int blockCountHx = reader.ReadInt32();
            short oldEncodingVersion = reader.ReadInt16();
            short oldSongId = reader.ReadInt16();
            byte[] oldGenre = reader.ReadBytes(20);
            int thumbnailSize = reader.ReadInt32();
            int fileVersion = reader.ReadInt32();
            byte[] titleBytes = reader.ReadBytes(64);
            byte[] artistBytes = reader.ReadBytes(32);
            byte[] noteArrangerBytes = reader.ReadBytes(32);
            byte[] ojmBytes = reader.ReadBytes(32);
            int coverSize = reader.ReadInt32();
            int durationEx = reader.ReadInt32();
            int durationNx = reader.ReadInt32();
            int durationHx = reader.ReadInt32();
            int blockOffsetEx = reader.ReadInt32();
            int blockOffsetNx = reader.ReadInt32();
            int blockOffsetHx = reader.ReadInt32();
            int coverOffset = reader.ReadInt32();

            string title = DecodeString(titleBytes);
            string artist = DecodeString(artistBytes);
            string noteArranger = DecodeString(noteArrangerBytes);
            string ojmName = DecodeString(ojmBytes);

            string genre = genreInt switch
            {
                0 => "Ballad",
                1 => "Rock",
                2 => "Dance",
                3 => "Techno",
                4 => "HipHop",
                5 => "Soul",
                6 => "Jazz",
                7 => "Funk",
                8 => "Classical",
                9 => "Traditional",
                _ => "Etc."
            };

            bool hasEx = blockCountEx > 0 && blockOffsetEx > 0;
            bool hasNx = blockCountNx > 0 && blockOffsetNx > 0;
            bool hasHx = blockCountHx > 0 && blockOffsetHx > 0;

            OjnDifficulty diff = preferredDifficulty ?? OjnDifficulty.HX;
            if (diff == OjnDifficulty.HX && !hasHx)
                diff = hasNx ? OjnDifficulty.NX : (hasEx ? OjnDifficulty.EX : OjnDifficulty.HX);
            else if (diff == OjnDifficulty.NX && !hasNx)
                diff = hasHx ? OjnDifficulty.HX : (hasEx ? OjnDifficulty.EX : OjnDifficulty.NX);
            else if (diff == OjnDifficulty.EX && !hasEx)
                diff = hasNx ? OjnDifficulty.NX : (hasHx ? OjnDifficulty.HX : OjnDifficulty.EX);

            int offset = 0;
            int blockCount = 0;
            int level = 0;

            if (diff == OjnDifficulty.EX && hasEx)
            {
                offset = blockOffsetEx;
                blockCount = blockCountEx;
                level = levelEx;
            }
            else if (diff == OjnDifficulty.NX && hasNx)
            {
                offset = blockOffsetNx;
                blockCount = blockCountNx;
                level = levelNx;
            }
            else if (diff == OjnDifficulty.HX && hasHx)
            {
                offset = blockOffsetHx;
                blockCount = blockCountHx;
                level = levelHx;
            }

            var chart = new BmsChart
            {
                FilePath = filePath,
                CurrentDifficulty = diff,
                Header = new BmsHeader
                {
                    Title = !string.IsNullOrEmpty(title) ? title : Path.GetFileNameWithoutExtension(filePath),
                    Artist = artist,
                    Noter = noteArranger,
                    Genre = genre,
                    InitialBpm = BmsChart.SanitizeBpm(bpm),
                    PlayLevel = level
                }
            };

            chart.AvailableDifficulties[(int)OjnDifficulty.EX] = hasEx;
            chart.AvailableDifficulties[(int)OjnDifficulty.NX] = hasNx;
            chart.AvailableDifficulties[(int)OjnDifficulty.HX] = hasHx;

            chart.DifficultyLevels[(int)OjnDifficulty.EX] = levelEx;
            chart.DifficultyLevels[(int)OjnDifficulty.NX] = levelNx;
            chart.DifficultyLevels[(int)OjnDifficulty.HX] = levelHx;

            chart.DifficultyNoteCounts[(int)OjnDifficulty.EX] = noteCountEx;
            chart.DifficultyNoteCounts[(int)OjnDifficulty.NX] = noteCountNx;
            chart.DifficultyNoteCounts[(int)OjnDifficulty.HX] = noteCountHx;

            var parsedEvents = new List<ParsedNoteEvent>();

            if (offset > 0 && blockCount > 0 && offset < data.Length)
            {
                ms.Position = offset;
                for (int b = 0; b < blockCount; b++)
                {
                    if (ms.Position + 8 > ms.Length) break;
                    int measure = reader.ReadInt32();
                    short channel = reader.ReadInt16();
                    short eventCount = reader.ReadInt16();

                    if (eventCount > 192) continue;

                    int dataLen = eventCount * 4;
                    if (ms.Position + dataLen > ms.Length) break;
                    byte[] blockData = reader.ReadBytes(dataLen);

                    for (int f = 0; f < eventCount; f++)
                    {
                        double position = (double)f / eventCount;

                        if (channel == 0 || channel == 1)
                        {
                            float val = BitConverter.ToSingle(blockData, f * 4);
                            if (val == 0) continue;

                            parsedEvents.Add(new ParsedNoteEvent
                            {
                                Measure = measure,
                                Channel = channel,
                                Position = position,
                                Value = val,
                                CellSize = eventCount
                            });
                        }
                        else
                        {
                            short val = BitConverter.ToInt16(blockData, f * 4);
                            if (val == 0) continue;

                            byte volPan = blockData[f * 4 + 2];
                            byte type = blockData[f * 4 + 3];

                            float sampleVal = (float)val - 1.0f;
                            if (type % 8 > 3 || type == 4)
                            {
                                sampleVal += 1000.0f;
                            }

                            float volume = ((volPan >> 4) & 0x0F) / 16.0f;
                            if (volume == 0.0f) volume = 1.0f;

                            float pan = (float)(volPan & 0x0F);
                            if (pan == 0.0f) pan = 8.0f;
                            pan = (pan - 8.0f) / 8.0f;

                            int evType = type % 4; // 2 = HoldStart, 3 = HoldEnd, default = Tap

                            parsedEvents.Add(new ParsedNoteEvent
                            {
                                Measure = measure,
                                Channel = channel,
                                Position = position,
                                Value = sampleVal,
                                CellSize = eventCount,
                                Volume = volume,
                                Pan = pan,
                                EventType = evType
                            });
                        }
                    }
                }
            }

            // Measure length and BPM changes must precede note events at the same position.
            parsedEvents.Sort((a, b) =>
            {
                double posA = a.Measure + a.Position;
                double posB = b.Measure + b.Position;
                int cmp = posA.CompareTo(posB);
                if (cmp != 0) return cmp;
                return a.Channel.CompareTo(b.Channel);
            });

            // Linear timing simulation matching O2Game OJN.cpp
            const double BeatsPerMsec = 4.0 * 60.0 * 1000.0;
            const double StartTime = 1500.0;

            double currentBPM = bpm;
            double measureFraction = 1.0;
            double measurePosition = 0.0;
            double timer = StartTime;
            double currentTick = 0.0;
            double measureStartTick = 0.0;
            double measureStartTimer = StartTime;

            chart.BpmChanges.Add(new BmsBpmChange
            {
                Tick = 0,
                TimeSeconds = StartTime / 1000.0,
                Bpm = BmsChart.SanitizeBpm(currentBPM)
            });

            double[] holdNotesTimer = new double[7];
            double[] holdNotesTick = new double[7];
            float[] holdNotesPos = new float[] { -1.0f, -1.0f, -1.0f, -1.0f, -1.0f, -1.0f, -1.0f };
            int[] holdSampleId = new int[7];

            int currentMeasure = 0;

            foreach (var ev in parsedEvents)
            {
                while (ev.Measure > currentMeasure)
                {
                    timer += (BeatsPerMsec * (measureFraction - measurePosition)) / currentBPM;
                    currentTick += (measureFraction - measurePosition) * 192.0;

                    chart.Measures.Add(new BmsMeasure
                    {
                        Index = currentMeasure,
                        LengthFactor = measureFraction,
                        StartTick = measureStartTick,
                        EndTick = currentTick,
                        StartTimeSeconds = measureStartTimer / 1000.0,
                        DurationSeconds = (timer - measureStartTimer) / 1000.0
                    });
                    chart.MeasureFactors[currentMeasure] = measureFraction;

                    currentMeasure++;
                    measurePosition = 0.0;
                    measureFraction = 1.0;
                    measureStartTimer = timer;
                    measureStartTick = currentTick;
                }

                double position = ev.Position * measureFraction;
                timer += (BeatsPerMsec * (position - measurePosition)) / currentBPM;
                currentTick += (position - measurePosition) * 192.0;
                measurePosition = position;

                if (ev.Channel == 0)
                {
                    measureFraction = ev.Value;
                }
                else if (ev.Channel == 1)
                {
                    currentBPM = ev.Value;
                    chart.BpmChanges.Add(new BmsBpmChange
                    {
                        Tick = currentTick,
                        TimeSeconds = timer / 1000.0,
                        Bpm = BmsChart.SanitizeBpm(currentBPM)
                    });
                }
                else if (ev.Channel < 9)
                {
                    int laneIndex = ev.Channel - 2;
                    if (laneIndex >= 0 && laneIndex < 7)
                    {
                        switch (ev.EventType)
                        {
                            case 2: // HoldStart
                                holdNotesTimer[laneIndex] = timer;
                                holdNotesTick[laneIndex] = currentTick;
                                holdNotesPos[laneIndex] = (float)(ev.Measure + ev.Position);
                                holdSampleId[laneIndex] = (int)ev.Value;
                                break;

                            case 3: // HoldEnd
                                if (holdNotesPos[laneIndex] != -1.0f)
                                {
                                    int sampleRef = (int)ev.Value;
                                    if (sampleRef == 0 && holdSampleId[laneIndex] > 0)
                                        sampleRef = holdSampleId[laneIndex];

                                    var note = new BmsNote
                                    {
                                        Lane = laneIndex + 1,
                                        Value = sampleRef,
                                        Tick = holdNotesTick[laneIndex],
                                        DurationTicks = Math.Max(0, currentTick - holdNotesTick[laneIndex]),
                                        TimeSeconds = holdNotesTimer[laneIndex] / 1000.0,
                                        DurationSeconds = Math.Max(0, (timer - holdNotesTimer[laneIndex]) / 1000.0),
                                        Volume = ev.Volume,
                                        Pan = ev.Pan,
                                        Measure = ev.Measure,
                                        Position = holdNotesPos[laneIndex]
                                    };
                                    chart.Notes.Add(note);
                                    holdNotesPos[laneIndex] = -1.0f;
                                }
                                break;

                            default:
                                var tapNote = new BmsNote
                                {
                                    Lane = laneIndex + 1,
                                    Value = (int)ev.Value,
                                    Tick = currentTick,
                                    DurationTicks = 0,
                                    TimeSeconds = timer / 1000.0,
                                    DurationSeconds = 0,
                                    Volume = ev.Volume,
                                    Pan = ev.Pan,
                                    Measure = ev.Measure,
                                    Position = (float)(ev.Measure + ev.Position)
                                };
                                chart.Notes.Add(tapNote);
                                break;
                        }
                    }
                }
                else
                {
                    var bgNote = new BmsNote
                    {
                        Lane = 0,
                        Value = (int)ev.Value,
                        Tick = currentTick,
                        DurationTicks = 0,
                        TimeSeconds = timer / 1000.0,
                        DurationSeconds = 0,
                        Volume = ev.Volume,
                        Pan = ev.Pan,
                        Measure = ev.Measure,
                        Position = (float)(ev.Measure + ev.Position)
                    };
                    chart.Notes.Add(bgNote);
                }
            }

            timer += (BeatsPerMsec * (measureFraction - measurePosition)) / currentBPM;
            currentTick += (measureFraction - measurePosition) * 192.0;
            chart.Measures.Add(new BmsMeasure
            {
                Index = currentMeasure,
                LengthFactor = measureFraction,
                StartTick = measureStartTick,
                EndTick = currentTick,
                StartTimeSeconds = measureStartTimer / 1000.0,
                DurationSeconds = (timer - measureStartTimer) / 1000.0
            });
            chart.MeasureFactors[currentMeasure] = measureFraction;

            for (int p = 1; p <= 2; p++)
            {
                currentMeasure++;
                measureStartTick = currentTick;
                measureStartTimer = timer;
                timer += (BeatsPerMsec * 1.0) / currentBPM;
                currentTick += 192.0;

                chart.Measures.Add(new BmsMeasure
                {
                    Index = currentMeasure,
                    LengthFactor = 1.0,
                    StartTick = measureStartTick,
                    EndTick = currentTick,
                    StartTimeSeconds = measureStartTimer / 1000.0,
                    DurationSeconds = (timer - measureStartTimer) / 1000.0
                });
                chart.MeasureFactors[currentMeasure] = 1.0;
            }

            chart.Notes.Sort((a, b) =>
            {
                int cmp = a.TimeSeconds.CompareTo(b.TimeSeconds);
                return cmp != 0 ? cmp : a.Lane.CompareTo(b.Lane);
            });

            if (audio != null)
            {
                string ojmPath = FindOjmPath(filePath, ojmName);
                if (File.Exists(ojmPath))
                {
                    var decodedSamples = OjmDecoder.LoadArchive(ojmPath);
                    audio.LoadArchiveSamples(ojmPath, decodedSamples);

                    foreach (var kvp in decodedSamples)
                    {
                        chart.Header.Wavs[kvp.Key] = $"ojm_{kvp.Key}.wav";
                    }
                }
                else
                {
                    Logger.Warn($"[OJN] OJM file not found for {filePath} (expected: '{ojmName}')");
                }
            }

            return chart;
        }

        public static string FindOjmPath(string ojnFilePath, string? ojmNameInHeader = null)
        {
            string dir = Path.GetDirectoryName(ojnFilePath) ?? "";
            
            if (!string.IsNullOrEmpty(ojmNameInHeader))
            {
                string cleanName = Path.GetFileName(ojmNameInHeader.Trim());
                if (!string.IsNullOrEmpty(cleanName))
                {
                    string headerOjm = Path.Combine(dir, cleanName);
                    if (File.Exists(headerOjm)) return headerOjm;

                    if (!headerOjm.EndsWith(".ojm", StringComparison.OrdinalIgnoreCase))
                    {
                        string headerWithExt = headerOjm + ".ojm";
                        if (File.Exists(headerWithExt)) return headerWithExt;
                    }

                    if (Directory.Exists(dir))
                    {
                        string searchName = cleanName;
                        if (!searchName.EndsWith(".ojm", StringComparison.OrdinalIgnoreCase))
                            searchName += ".ojm";

                        foreach (var f in Directory.GetFiles(dir, "*.*"))
                        {
                            if (string.Equals(Path.GetFileName(f), searchName, StringComparison.OrdinalIgnoreCase))
                                return f;
                        }
                    }
                }
            }

            string sameName = Path.ChangeExtension(ojnFilePath, ".ojm");
            if (File.Exists(sameName)) return sameName;

            if (Directory.Exists(dir))
            {
                string targetFileName = Path.GetFileName(sameName);
                foreach (var f in Directory.GetFiles(dir, "*.*"))
                {
                    if (string.Equals(Path.GetFileName(f), targetFileName, StringComparison.OrdinalIgnoreCase))
                        return f;
                }

                // Fallback: look for any .ojm file in the song folder.
                foreach (var f in Directory.GetFiles(dir, "*.*"))
                {
                    if (f.EndsWith(".ojm", StringComparison.OrdinalIgnoreCase))
                        return f;
                }
            }

            return sameName;
        }

        public static byte[] Decrypt(byte[] fileBytes)
        {
            if (fileBytes.Length < 7) return fileBytes;

            // Encrypted OJN charts start with the "new" magic signature.
            if (fileBytes[0] != (byte)'n' || fileBytes[1] != (byte)'e' || fileBytes[2] != (byte)'w')
            {
                return fileBytes;
            }

            byte blockSize = fileBytes[3];
            byte mainKey = fileBytes[4];
            byte midKey = fileBytes[5];
            byte initialKey = fileBytes[6];

            if (blockSize == 0) return fileBytes;

            byte[] key = new byte[blockSize];
            Array.Fill(key, mainKey);
            key[0] = initialKey;
            key[blockSize / 2] = midKey;

            int totalSize = fileBytes.Length;
            int outputLen = totalSize - 7;
            byte[] output = new byte[outputLen];

            for (int i = 0; i < outputLen; i += blockSize)
            {
                for (int j = 0; j < blockSize; j++)
                {
                    int offset = i + j;
                    if (offset >= outputLen) break;

                    output[offset] = (byte)(fileBytes[totalSize - (offset + 1)] ^ key[j]);
                }
            }

            return output;
        }

        private static string DecodeString(byte[] bytes)
        {
            int len = 0;
            while (len < bytes.Length && bytes[len] != 0) len++;
            if (len == 0) return string.Empty;

            try
            {
                var enc = Encoding.GetEncoding(949); // CP949 / EUC-KR for Korean chart metadata
                return enc.GetString(bytes, 0, len).Trim();
            }
            catch
            {
                return Encoding.Default.GetString(bytes, 0, len).Trim();
            }
        }

        private class ParsedNoteEvent
        {
            public int Measure;
            public int Channel;
            public double Position;
            public float Value;
            public int CellSize;
            public float Volume = 1.0f;
            public float Pan = 0.0f;
            public int EventType = 0;
        }
    }
}
