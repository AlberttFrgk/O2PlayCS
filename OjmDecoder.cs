using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace O2Play
{
    public static class OjmDecoder
    {
        private static readonly byte[] WaveRearrangeTable = new byte[]
        {
            0x10, 0x0E, 0x02, 0x09, 0x04, 0x00, 0x07, 0x01,
            0x06, 0x08, 0x0F, 0x0A, 0x05, 0x0C, 0x03, 0x0D,
            0x0B, 0x07, 0x02, 0x0A, 0x0B, 0x03, 0x05, 0x0D,
            0x08, 0x04, 0x00, 0x0C, 0x06, 0x0F, 0x0E, 0x10,
            0x01, 0x09, 0x0C, 0x0D, 0x03, 0x00, 0x06, 0x09,
            0x0A, 0x01, 0x07, 0x08, 0x10, 0x02, 0x0B, 0x0E,
            0x04, 0x0F, 0x05, 0x08, 0x03, 0x04, 0x0D, 0x06,
            0x05, 0x0B, 0x10, 0x02, 0x0C, 0x07, 0x09, 0x0A,
            0x0F, 0x0E, 0x00, 0x01, 0x0F, 0x02, 0x0C, 0x0D,
            0x00, 0x04, 0x01, 0x05, 0x07, 0x03, 0x09, 0x10,
            0x06, 0x0B, 0x0A, 0x08, 0x0E, 0x00, 0x04, 0x0B,
            0x10, 0x0F, 0x0D, 0x0C, 0x06, 0x05, 0x07, 0x01,
            0x02, 0x03, 0x08, 0x09, 0x0A, 0x0E, 0x03, 0x10,
            0x08, 0x07, 0x06, 0x09, 0x0E, 0x0D, 0x00, 0x0A,
            0x0B, 0x04, 0x05, 0x0C, 0x02, 0x01, 0x0F, 0x04,
            0x0E, 0x10, 0x0F, 0x05, 0x08, 0x07, 0x0B, 0x00,
            0x01, 0x06, 0x02, 0x0C, 0x09, 0x03, 0x0A, 0x0D,
            0x06, 0x0D, 0x0E, 0x07, 0x10, 0x0A, 0x0B, 0x00,
            0x01, 0x0C, 0x0F, 0x02, 0x03, 0x08, 0x09, 0x04,
            0x05, 0x0A, 0x0C, 0x00, 0x08, 0x09, 0x0D, 0x03,
            0x04, 0x05, 0x10, 0x0E, 0x0F, 0x01, 0x02, 0x0B,
            0x06, 0x07, 0x05, 0x06, 0x0C, 0x04, 0x0D, 0x0F,
            0x07, 0x0E, 0x08, 0x01, 0x09, 0x02, 0x10, 0x0A,
            0x0B, 0x00, 0x03, 0x0B, 0x0F, 0x04, 0x0E, 0x03,
            0x01, 0x00, 0x02, 0x0D, 0x0C, 0x06, 0x07, 0x05,
            0x10, 0x09, 0x08, 0x0A, 0x03, 0x02, 0x01, 0x00,
            0x04, 0x0C, 0x0D, 0x0B, 0x10, 0x05, 0x06, 0x0F,
            0x0E, 0x07, 0x09, 0x0A, 0x08, 0x09, 0x0A, 0x00,
            0x07, 0x08, 0x06, 0x10, 0x03, 0x04, 0x01, 0x02,
            0x05, 0x0B, 0x0E, 0x0F, 0x0D, 0x0C, 0x0A, 0x06,
            0x09, 0x0C, 0x0B, 0x10, 0x07, 0x08, 0x00, 0x0F,
            0x03, 0x01, 0x02, 0x05, 0x0D, 0x0E, 0x04, 0x0D,
            0x00, 0x01, 0x0E, 0x02, 0x03, 0x08, 0x0B, 0x07,
            0x0C, 0x09, 0x05, 0x0A, 0x0F, 0x04, 0x06, 0x10,
            0x01, 0x0E, 0x02, 0x03, 0x0D, 0x0B, 0x07, 0x00,
            0x08, 0x0C, 0x09, 0x06, 0x0F, 0x10, 0x05, 0x0A,
            0x04, 0x00
        };

        private static readonly byte[] MaskScramble1 = new byte[] { 0x73, 0x63, 0x72, 0x61, 0x6D, 0x62, 0x6C, 0x65, 0x31 };
        private static readonly byte[] MaskScramble2 = new byte[] { 0x73, 0x63, 0x72, 0x61, 0x6D, 0x62, 0x6C, 0x65, 0x32 };
        private static readonly byte[] MaskDecode    = new byte[] { 0x64, 0x65, 0x63, 0x6F, 0x64, 0x65 };
        private static readonly byte[] MaskDecrypt   = new byte[] { 0x64, 0x65, 0x63, 0x72, 0x79, 0x70, 0x74 };
        private static readonly byte[] MaskNami      = new byte[] { 0x6E, 0x61, 0x6D, 0x69 };
        private static readonly byte[] Mask0412      = new byte[] { 0x30, 0x34, 0x31, 0x32 };

        private static void M30Xor(byte[] data, byte[] xorKey)
        {
            for (int i = 0; i + 3 < data.Length; i += 4)
            {
                data[i]     ^= xorKey[i % 4];
                data[i + 1] ^= xorKey[(i + 1) % 4];
                data[i + 2] ^= xorKey[(i + 2) % 4];
                data[i + 3] ^= xorKey[(i + 3) % 4];
            }
        }

        private static readonly Dictionary<string, (DateTime lastWriteTime, Dictionary<int, byte[]> samples)> _archiveCache = new(StringComparer.OrdinalIgnoreCase);

        public static Dictionary<int, byte[]> LoadArchive(string filePath)
        {
            var samples = new Dictionary<int, byte[]>();
            if (!File.Exists(filePath)) return samples;

            try
            {
                var lastWrite = File.GetLastWriteTimeUtc(filePath);
                lock (_archiveCache)
                {
                    if (_archiveCache.TryGetValue(filePath, out var cached) && cached.lastWriteTime == lastWrite)
                    {
                        return cached.samples;
                    }
                }

                byte[] fileBytes = File.ReadAllBytes(filePath);
                if (fileBytes.Length < 4) return samples;

                int signature = BitConverter.ToInt32(fileBytes, 0);

                if (signature == 0x0030334D)
                {
                    DecodeM30(fileBytes, samples);
                }
                else if (signature == 0x00434D4F)
                {
                    DecodeOmc(fileBytes, true, samples);
                }
                else if (signature == 0x004D4A4F)
                {
                    DecodeOmc(fileBytes, false, samples);
                }

                lock (_archiveCache)
                {
                    _archiveCache[filePath] = (lastWrite, samples);
                }

                return samples;
            }
            catch (Exception ex)
            {
                Logger.Error($"[OJM] Error loading archive '{filePath}': {ex.Message}");
                return samples;
            }
        }

        private static void DecodeM30(byte[] data, Dictionary<int, byte[]> samples)
        {
            if (data.Length < 28) return;
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            int signature = reader.ReadInt32();
            int fileFormatVersion = reader.ReadInt32();
            int encryptionFlag = reader.ReadInt32();
            int sampleCount = reader.ReadInt32();
            int sampleOffset = reader.ReadInt32();
            int sampleSize = reader.ReadInt32();
            int padding = reader.ReadInt32();

            for (int i = 0; i < sampleCount; i++)
            {
                if (ms.Position + 52 > ms.Length) break;

                // M30SampleHeader is 52 bytes
                byte[] sampleName = reader.ReadBytes(32);
                int chunkSize = reader.ReadInt32();
                short codecCode = reader.ReadInt16();
                short unkFixed = reader.ReadInt16();
                int unkMusicFlag = reader.ReadInt32();
                short valueRef = reader.ReadInt16();
                short unkFixed2 = reader.ReadInt16();
                int pcmSamples = reader.ReadInt32();

                if (chunkSize <= 0)
                {
                    continue;
                }

                if (ms.Position + chunkSize > ms.Length) break;
                byte[] buffer = reader.ReadBytes(chunkSize);

                switch (encryptionFlag)
                {
                    case 0:
                        break;
                    case 1:
                        M30Xor(buffer, MaskScramble1);
                        break;
                    case 2:
                        M30Xor(buffer, MaskScramble2);
                        break;
                    case 4:
                        M30Xor(buffer, MaskDecode);
                        break;
                    case 8:
                        M30Xor(buffer, MaskDecrypt);
                        break;
                    case 16:
                        M30Xor(buffer, MaskNami);
                        break;
                    case 32:
                        M30Xor(buffer, Mask0412);
                        break;
                }

                int refId = valueRef;
                if (codecCode == 0) // OGG Sample
                {
                    refId += 1000;
                }

                samples[refId] = buffer;
            }
        }

        private static void DecodeOmc(byte[] data, bool encrypted, Dictionary<int, byte[]> samples)
        {
            if (data.Length < 20) return;
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            int signature = reader.ReadInt32();
            short wavSizes = reader.ReadInt16();
            short oggSizes = reader.ReadInt16();
            int wavOffset = reader.ReadInt32();
            int oggOffset = reader.ReadInt32();
            int fileSize = reader.ReadInt32();

            if (wavOffset >= 0 && wavOffset < data.Length)
            {
                ms.Position = wavOffset;
                int accKeyByte = 0xFF;
                int accCounter = 0;
                int valueRef = 0;

                for (int i = 0; i < wavSizes; i++)
                {
                    if (ms.Position + 56 > ms.Length) break;

                    byte[] sampleName = reader.ReadBytes(32);
                    short audioFormat = reader.ReadInt16();
                    short channels = reader.ReadInt16();
                    int sampleRate = reader.ReadInt32();
                    int byteRate = reader.ReadInt32();
                    short blockAlign = reader.ReadInt16();
                    short bitsPerSample = reader.ReadInt16();
                    int unk1 = reader.ReadInt32();
                    int chunkSize = reader.ReadInt32();

                    if (chunkSize <= 0)
                    {
                        valueRef++;
                        continue;
                    }
                    if (ms.Position + chunkSize > ms.Length)
                    {
                        break;
                    }

                    byte[] buffer = reader.ReadBytes(chunkSize);

                    if (encrypted)
                    {
                        buffer = DecodeWave(buffer, ref accKeyByte, ref accCounter);
                        chunkSize = buffer.Length;
                    }

                    // Assemble standard RIFF/WAVE header around the decoded PCM payload
                    using var waveMs = new MemoryStream(chunkSize + 44);
                    using var writer = new BinaryWriter(waveMs);
                    writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                    writer.Write(chunkSize + 36);
                    writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                    writer.Write(Encoding.ASCII.GetBytes("fmt "));
                    writer.Write((int)16);
                    writer.Write(audioFormat);
                    writer.Write(channels);
                    writer.Write(sampleRate);
                    writer.Write(byteRate);
                    writer.Write(blockAlign);
                    writer.Write(bitsPerSample);
                    writer.Write(Encoding.ASCII.GetBytes("data"));
                    writer.Write(chunkSize);
                    writer.Write(buffer);

                    samples[valueRef++] = waveMs.ToArray();
                }
            }

            if (oggOffset >= 0 && oggOffset < data.Length)
            {
                ms.Position = oggOffset;
                int valueRef = 1000;

                for (int i = 0; i < oggSizes; i++)
                {
                    if (ms.Position + 36 > ms.Length) break;

                    byte[] sampleName = reader.ReadBytes(32);
                    int sampleSize = reader.ReadInt32();

                    if (sampleSize <= 0)
                    {
                        valueRef++;
                        continue;
                    }
                    if (ms.Position + sampleSize > ms.Length)
                    {
                        break;
                    }

                    byte[] buffer = reader.ReadBytes(sampleSize);
                    samples[valueRef++] = buffer;
                }
            }
        }

        private static byte[] DecodeWave(byte[] inData, ref int accKeyByte, ref int accCounter)
        {
            int len = inData.Length;
            byte[] rearranged = new byte[len];
            int key = ((len % 17) << 4) + (len % 17);
            int blockSz = len / 17;

            for (int i = 0; i < 17; i++)
            {
                int inOffset = blockSz * i;
                int outOffset = blockSz * WaveRearrangeTable[key % WaveRearrangeTable.Length];
                if (outOffset + blockSz <= len && inOffset + blockSz <= len)
                {
                    Buffer.BlockCopy(inData, inOffset, rearranged, outOffset, blockSz);
                }
                key++;
            }

            byte[] result = new byte[len];
            for (int i = 0; i < len; i++)
            {
                byte tmp = rearranged[i];
                byte thisChar = tmp;

                if (((accKeyByte << accCounter) & 0x80) != 0)
                {
                    thisChar = (byte)~thisChar;
                }

                result[i] = thisChar;
                accCounter++;

                if (accCounter > 7)
                {
                    accCounter = 0;
                    accKeyByte = tmp;
                }
            }

            return result;
        }
    }
}
