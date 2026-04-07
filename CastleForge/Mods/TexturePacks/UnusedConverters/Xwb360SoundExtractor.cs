/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Linq;
using System.Text;
using System.IO;
using System;

namespace TexturePacks.UnusedConverters
{
    /// <summary>
    /// Preserved standalone-style XWB extractor for standard XACT wave banks.
    /// Intended for archival / one-off conversion use and not wired into the TexturePacks runtime.
    /// </summary>
    internal static class Xwb360SoundExtractor
    {
        /// <summary>
        /// Helper entry points for manually extracting Xbox 360 / standard XWB banks.
        /// </summary>
        #region Converter Entry Points

        /// <summary>
        /// Extracts a standard XWB wave bank into the specified output folder.
        /// Supports:
        ///  - "WBND" little-endian banks.
        ///  - "DNBW" big-endian / Xbox 360 style banks.
        /// Outputs:
        ///  - PCM entries as .wav
        ///  - ADPCM entries as .wav
        ///  - WMA / XMA entries as raw payloads with metadata sidecars
        /// </summary>
        public static void Extract(string xwbPath, string outputFolder)
        {
            ExtractXwb(xwbPath, outputFolder);
        }

        /// <summary>
        /// Convenience wrapper that mirrors a console-tool flow while preserving any failure state for the caller.
        /// </summary>
        public static bool TryExtract(string xwbPath, string outputFolder, out string error)
        {
            try
            {
                ExtractXwb(xwbPath, outputFolder);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
        #endregion

        /// <summary>
        /// Constants that describe bank-level XWB flags.
        /// </summary>
        #region Constants: Bank Flags

        /// <summary>
        /// Bank contains a names segment for per-entry names.
        /// </summary>
        private const uint FLAG_ENTRYNAMES = 0x00010000;

        /// <summary>
        /// Bank uses compact metadata layout.
        /// This preserved extractor does not handle compact banks.
        /// </summary>
        private const uint FLAG_COMPACT = 0x00020000;
        #endregion

        /// <summary>
        /// Constants that describe the packed XWB format tags.
        /// </summary>
        #region Constants: Format Tags

        /// <summary>
        /// PCM format tag.
        /// </summary>
        private const uint TAG_PCM = 0;

        /// <summary>
        /// XMA format tag.
        /// </summary>
        private const uint TAG_XMA = 1;

        /// <summary>
        /// Microsoft ADPCM format tag.
        /// </summary>
        private const uint TAG_ADPCM = 2;

        /// <summary>
        /// WMA / xWMA format tag.
        /// </summary>
        private const uint TAG_WMA = 3;
        #endregion

        /// <summary>
        /// Standard ADPCM coefficients used when emitting WAV metadata for ADPCM entries.
        /// </summary>
        #region Constants: ADPCM Coefficients

        /// <summary>
        /// Standard Microsoft ADPCM coefficient table (coef1 values).
        /// </summary>
        private static readonly short[] AdpcmCoef1 = { 256, 512, 0, 192, 240, 460, 392 };

        /// <summary>
        /// Standard Microsoft ADPCM coefficient table (coef2 values).
        /// </summary>
        private static readonly short[] AdpcmCoef2 = { 0, -256, 0, 64, 0, -208, -232 };
        #endregion

        /// <summary>
        /// This is the main extraction logic for the preserved XWB utility.
        /// </summary>
        #region Extraction Core

        /// <summary>
        /// Extracts a standard XWB wave bank into the specified output folder.
        /// </summary>
        private static void ExtractXwb(string xwbPath, string outputFolder)
        {
            if (string.IsNullOrWhiteSpace(xwbPath))
                throw new ArgumentException("Input XWB path is required.", nameof(xwbPath));

            if (string.IsNullOrWhiteSpace(outputFolder))
                throw new ArgumentException("Output folder is required.", nameof(outputFolder));

            if (!File.Exists(xwbPath))
                throw new FileNotFoundException("Input file not found.", xwbPath);

            Directory.CreateDirectory(outputFolder);

            using (FileStream fs = File.OpenRead(xwbPath))
            using (BinaryReader br = new BinaryReader(fs, Encoding.ASCII, true))
            {
                string signature = Encoding.ASCII.GetString(br.ReadBytes(4));
                bool bigEndian;

                if (signature == "WBND")
                    bigEndian = false;
                else if (signature == "DNBW")
                    bigEndian = true;
                else
                    throw new InvalidDataException("Not an XACT wave bank (.xwb). Signature was '" + signature + "'.");

                uint version = ReadUInt32(br, bigEndian);
                uint headerVersion = ReadUInt32(br, bigEndian);

                Segment[] segments = new Segment[5];
                for (int i = 0; i < segments.Length; i++)
                {
                    segments[i] = new Segment
                    {
                        Offset = ReadUInt32(br, bigEndian),
                        Length = ReadUInt32(br, bigEndian)
                    };
                }

                Console.WriteLine("Signature={0}", signature);
                Console.WriteLine("Version={0}, HeaderVersion={1}", version, headerVersion);
                for (int i = 0; i < segments.Length; i++)
                    Console.WriteLine("Seg{0}: Off=0x{1:X8} Len=0x{2:X8}", i, segments[i].Offset, segments[i].Length);

                fs.Position = segments[0].Offset;
                BankData bank = new BankData
                {
                    Flags                    = ReadUInt32(br, bigEndian),
                    EntryCount               = ReadUInt32(br, bigEndian),
                    BankName                 = ReadFixedNullTerminatedString(br, 64),
                    EntryMetaDataElementSize = ReadUInt32(br, bigEndian),
                    EntryNameElementSize     = ReadUInt32(br, bigEndian),
                    Alignment                = ReadUInt32(br, bigEndian),
                    CompactFormat            = ReadUInt32(br, bigEndian),
                    BuildTimeLow             = ReadUInt32(br, bigEndian),
                    BuildTimeHigh            = ReadUInt32(br, bigEndian)
                };

                Console.WriteLine("Bank      : " + bank.BankName);
                Console.WriteLine("Version   : " + version + " (header " + headerVersion + ")");
                Console.WriteLine("Entries   : " + bank.EntryCount);
                Console.WriteLine("Flags     : 0x" + bank.Flags.ToString("X8"));
                Console.WriteLine("Meta Size : " + bank.EntryMetaDataElementSize);
                Console.WriteLine("Name Size : " + bank.EntryNameElementSize);
                Console.WriteLine();

                if ((bank.Flags & FLAG_COMPACT) != 0)
                    throw new NotSupportedException("Compact wave banks are not handled by this preserved extractor yet.");

                if (bank.EntryMetaDataElementSize < 24)
                    throw new NotSupportedException("This extractor expects standard 24-byte entry metadata.");

                string[] names = ReadEntryNames(fs, br, bank, segments);

                for (int i = 0; i < bank.EntryCount; i++)
                {
                    fs.Position = segments[1].Offset + (i * bank.EntryMetaDataElementSize);

                    Entry entry = new Entry
                    {
                        FlagsAndDuration = ReadUInt32(br, bigEndian),
                        Format           = ReadUInt32(br, bigEndian),
                        PlayOffset       = ReadUInt32(br, bigEndian),
                        PlayLength       = ReadUInt32(br, bigEndian),
                        LoopStart        = ReadUInt32(br, bigEndian),
                        LoopLength       = ReadUInt32(br, bigEndian)
                    };

                    uint formatTag = GetFormatTag(entry.Format);
                    int channels = GetChannels(entry.Format);
                    int sampleRate = GetSampleRate(entry.Format);
                    int blockAlign = GetBlockAlign(entry.Format);
                    int bitsPerSample = GetBitsPerSample(entry.Format);
                    int avgBytesPerSecond = GetAvgBytesPerSecond(entry.Format);
                    uint durationSamples = GetDurationSamples(entry.FlagsAndDuration);

                    long dataOffset = (long)segments[4].Offset + entry.PlayOffset;
                    int dataLength = checked((int)entry.PlayLength);

                    string rawName = names[i];
                    if (string.IsNullOrWhiteSpace(rawName))
                        rawName = bank.BankName + "_" + i.ToString("D4");

                    string safeName = i.ToString("D4") + "_" + SanitizeFileName(rawName);
                    string basePath = Path.Combine(outputFolder, safeName);

                    Console.WriteLine(
                        "[{0:D4}] {1} | Tag={2} Ch={3} Hz={4} Len={5}",
                        i,
                        rawName,
                        formatTag,
                        channels,
                        sampleRate,
                        dataLength);

                    switch (formatTag)
                    {
                        case TAG_PCM:
                            WritePcmWave(
                                fs,
                                dataOffset,
                                dataLength,
                                basePath + ".wav",
                                channels,
                                sampleRate,
                                blockAlign,
                                bitsPerSample,
                                bigEndian);
                            break;

                        case TAG_ADPCM:
                            WriteAdpcmWave(
                                fs,
                                dataOffset,
                                dataLength,
                                basePath + ".wav",
                                channels,
                                sampleRate,
                                blockAlign,
                                avgBytesPerSecond,
                                durationSamples);
                            break;

                        case TAG_WMA:
                            WriteRawWithMetadata(
                                fs,
                                dataOffset,
                                dataLength,
                                basePath + ".xwma.bin",
                                "WMA/xWMA entry dumped as raw payload by preserved extractor.");
                            WriteMetadata(
                                basePath + ".txt",
                                rawName,
                                entry,
                                formatTag,
                                channels,
                                sampleRate,
                                blockAlign,
                                bitsPerSample,
                                avgBytesPerSecond,
                                durationSamples);
                            break;

                        case TAG_XMA:
                            WriteRawWithMetadata(
                                fs,
                                dataOffset,
                                dataLength,
                                basePath + ".xma.bin",
                                "XMA entry dumped as raw payload by preserved extractor.");
                            WriteMetadata(
                                basePath + ".txt",
                                rawName,
                                entry,
                                formatTag,
                                channels,
                                sampleRate,
                                blockAlign,
                                bitsPerSample,
                                avgBytesPerSecond,
                                durationSamples);
                            break;

                        default:
                            WriteRawWithMetadata(
                                fs,
                                dataOffset,
                                dataLength,
                                basePath + ".bin",
                                "Unknown entry format; dumped as raw bytes.");
                            WriteMetadata(
                                basePath + ".txt",
                                rawName,
                                entry,
                                formatTag,
                                channels,
                                sampleRate,
                                blockAlign,
                                bitsPerSample,
                                avgBytesPerSecond,
                                durationSamples);
                            break;
                    }
                }
            }
        }
        #endregion

        /// <summary>
        /// Helpers for reading packed XWB values while honoring endianness.
        /// </summary>
        #region Binary Readers

        /// <summary>
        /// Reads a 16-bit unsigned integer using the selected bank endianness.
        /// </summary>
        private static ushort ReadUInt16(BinaryReader br, bool bigEndian)
        {
            byte[] bytes = br.ReadBytes(2);
            if (bytes.Length != 2)
                throw new EndOfStreamException();

            if (bigEndian)
                Array.Reverse(bytes);

            return BitConverter.ToUInt16(bytes, 0);
        }

        /// <summary>
        /// Reads a 32-bit unsigned integer using the selected bank endianness.
        /// </summary>
        private static uint ReadUInt32(BinaryReader br, bool bigEndian)
        {
            byte[] bytes = br.ReadBytes(4);
            if (bytes.Length != 4)
                throw new EndOfStreamException();

            if (bigEndian)
                Array.Reverse(bytes);

            return BitConverter.ToUInt32(bytes, 0);
        }
        #endregion

        /// <summary>
        /// Helpers that read name metadata and other bank-level descriptors.
        /// </summary>
        #region Bank Metadata Readers

        /// <summary>
        /// Reads per-entry names when the wave bank contains a names segment.
        /// Returns empty fallback names otherwise.
        /// </summary>
        private static string[] ReadEntryNames(FileStream fs, BinaryReader br, BankData bank, Segment[] segments)
        {
            string[] names = Enumerable.Repeat(string.Empty, checked((int)bank.EntryCount)).ToArray();

            bool hasEntryNames =
                (bank.Flags & FLAG_ENTRYNAMES) != 0 &&
                bank.EntryNameElementSize > 0 &&
                segments[3].Length > 0;

            if (!hasEntryNames)
                return names;

            for (int i = 0; i < bank.EntryCount; i++)
            {
                fs.Position = segments[3].Offset + (i * bank.EntryNameElementSize);
                names[i] = ReadFixedNullTerminatedString(br, checked((int)bank.EntryNameElementSize));
            }

            return names;
        }
        #endregion

        /// <summary>
        /// Helpers that emit WAV files, raw payload dumps, and sidecar metadata.
        /// </summary>
        #region Writers: WAV / Raw Output

        /// <summary>
        /// Writes a PCM entry as a standard WAV file.
        ///
        /// Notes:
        /// - Big-endian 16-bit PCM payloads are byte-swapped in-place before writing.
        /// - 8-bit PCM is left unchanged.
        /// </summary>
        private static void WritePcmWave(
            FileStream fs,
            long dataOffset,
            int dataLength,
            string outPath,
            int channels,
            int sampleRate,
            int blockAlign,
            int bitsPerSample,
            bool bigEndian)
        {
            byte[] audioData = ReadBytes(fs, dataOffset, dataLength);

            // Xbox 360 big-endian XWB PCM payloads need sample byte-swapping for 16-bit PCM.
            if (bigEndian && bitsPerSample == 16)
                Swap16InPlace(audioData);

            using (FileStream outFs = File.Create(outPath))
            using (BinaryWriter bw = new BinaryWriter(outFs, Encoding.ASCII))
            {
                int riffSize = 4 + (8 + 16) + (8 + dataLength);

                WriteFourCC(bw, "RIFF");
                bw.Write(riffSize);
                WriteFourCC(bw, "WAVE");

                WriteFourCC(bw, "fmt ");
                bw.Write(16);
                bw.Write((ushort)1); // PCM
                bw.Write((ushort)channels);
                bw.Write(sampleRate);
                bw.Write(sampleRate * blockAlign);
                bw.Write((ushort)blockAlign);
                bw.Write((ushort)bitsPerSample);

                WriteFourCC(bw, "data");
                bw.Write(dataLength);
                bw.Write(audioData);
            }
        }

        /// <summary>
        /// Swaps each 16-bit sample in-place.
        /// Used for big-endian PCM payload conversion.
        /// </summary>
        private static void Swap16InPlace(byte[] data)
        {
            for (int i = 0; i + 1 < data.Length; i += 2)
            {
                (data[i + 1], data[i]) = (data[i], data[i + 1]);
            }
        }

        /// <summary>
        /// Writes an ADPCM entry as a standard WAV file using a Microsoft ADPCM fmt chunk.
        /// </summary>
        private static void WriteAdpcmWave(
            FileStream fs,
            long dataOffset,
            int dataLength,
            string outPath,
            int channels,
            int sampleRate,
            int blockAlign,
            int avgBytesPerSecond,
            uint durationSamples)
        {
            byte[] audioData = ReadBytes(fs, dataOffset, dataLength);
            ushort samplesPerBlock = checked((ushort)AdpcmSamplesPerBlock(channels, blockAlign));

            using (FileStream outFs = File.Create(outPath))
            using (BinaryWriter bw = new BinaryWriter(outFs, Encoding.ASCII))
            {
                const int fmtSize = 50;
                const int factSize = 4;
                int riffSize = 4 + (8 + fmtSize) + (8 + factSize) + (8 + dataLength);

                WriteFourCC(bw, "RIFF");
                bw.Write(riffSize);
                WriteFourCC(bw, "WAVE");

                WriteFourCC(bw, "fmt ");
                bw.Write(fmtSize);
                bw.Write((ushort)2); // Microsoft ADPCM
                bw.Write((ushort)channels);
                bw.Write(sampleRate);
                bw.Write(avgBytesPerSecond);
                bw.Write((ushort)blockAlign);
                bw.Write((ushort)4);  // ADPCM is always 4 bits per sample
                bw.Write((ushort)32); // cbSize
                bw.Write(samplesPerBlock);
                bw.Write((ushort)7);  // fixed coefficient count

                for (int i = 0; i < 7; i++)
                {
                    bw.Write(AdpcmCoef1[i]);
                    bw.Write(AdpcmCoef2[i]);
                }

                WriteFourCC(bw, "fact");
                bw.Write(factSize);
                bw.Write(durationSamples);

                WriteFourCC(bw, "data");
                bw.Write(dataLength);
                bw.Write(audioData);
            }
        }

        /// <summary>
        /// Writes raw entry bytes to disk and emits a small note alongside them.
        /// Used for unsupported or partially supported encoded formats.
        /// </summary>
        private static void WriteRawWithMetadata(FileStream fs, long dataOffset, int dataLength, string outPath, string headerNote)
        {
            byte[] audioData = ReadBytes(fs, dataOffset, dataLength);
            File.WriteAllBytes(outPath, audioData);

            string notePath = Path.ChangeExtension(outPath, ".note.txt");
            File.WriteAllText(notePath, headerNote + Environment.NewLine, Encoding.UTF8);
        }

        /// <summary>
        /// Writes a metadata sidecar text file describing the extracted entry.
        /// </summary>
        private static void WriteMetadata(
            string outPath,
            string name,
            Entry entry,
            uint formatTag,
            int channels,
            int sampleRate,
            int blockAlign,
            int bitsPerSample,
            int avgBytesPerSecond,
            uint durationSamples)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Name            : " + name);
            sb.AppendLine("FormatTag       : " + formatTag);
            sb.AppendLine("Channels        : " + channels);
            sb.AppendLine("SampleRate      : " + sampleRate);
            sb.AppendLine("BlockAlign      : " + blockAlign);
            sb.AppendLine("BitsPerSample   : " + bitsPerSample);
            sb.AppendLine("AvgBytesPerSec  : " + avgBytesPerSecond);
            sb.AppendLine("DurationSamples : " + durationSamples);
            sb.AppendLine("PlayOffset      : " + entry.PlayOffset);
            sb.AppendLine("PlayLength      : " + entry.PlayLength);
            sb.AppendLine("LoopStart       : " + entry.LoopStart);
            sb.AppendLine("LoopLength      : " + entry.LoopLength);
            File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);
        }
        #endregion

        /// <summary>
        /// Shared low-level helpers used throughout extraction.
        /// </summary>
        #region Low-Level Helpers

        /// <summary>
        /// Reads an exact byte range from the file stream.
        /// Throws if the requested range cannot be fully read.
        /// </summary>
        private static byte[] ReadBytes(FileStream fs, long offset, int length)
        {
            byte[] buffer = new byte[length];
            fs.Position = offset;

            int read = 0;
            while (read < length)
            {
                int chunk = fs.Read(buffer, read, length - read);
                if (chunk <= 0)
                    throw new EndOfStreamException("Unexpected end of file while reading wave data.");

                read += chunk;
            }

            return buffer;
        }

        /// <summary>
        /// Reads a fixed-width null-terminated ASCII string.
        /// </summary>
        private static string ReadFixedNullTerminatedString(BinaryReader br, int byteCount)
        {
            byte[] bytes = br.ReadBytes(byteCount);
            int length = Array.IndexOf(bytes, (byte)0);
            if (length < 0)
                length = bytes.Length;

            return Encoding.ASCII.GetString(bytes, 0, length).Trim();
        }

        /// <summary>
        /// Writes a FOURCC value as ASCII bytes.
        /// </summary>
        private static void WriteFourCC(BinaryWriter bw, string value)
        {
            bw.Write(Encoding.ASCII.GetBytes(value));
        }

        /// <summary>
        /// Replaces invalid file-name characters with underscores.
        /// </summary>
        private static string SanitizeFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder(name.Length);

            foreach (char c in name)
                sb.Append(invalid.Contains(c) ? '_' : c);

            return sb.ToString().Trim();
        }
        #endregion

        /// <summary>
        /// Helpers that decode the packed XWB mini-format fields.
        /// </summary>
        #region XWB Mini-Format Decoding

        /// <summary>
        /// Extracts the XWB format tag from the packed format field.
        /// </summary>
        private static uint GetFormatTag(uint format)
        {
            return format & 0x3;
        }

        /// <summary>
        /// Extracts channel count from the packed format field.
        /// </summary>
        private static int GetChannels(uint format)
        {
            return (int)((format >> 2) & 0x7);
        }

        /// <summary>
        /// Extracts sample rate from the packed format field.
        /// </summary>
        private static int GetSampleRate(uint format)
        {
            return (int)((format >> 5) & 0x3FFFF);
        }

        /// <summary>
        /// Computes block alignment from the packed format field.
        /// Behavior varies by codec tag.
        /// </summary>
        private static int GetBlockAlign(uint format)
        {
            uint tag = GetFormatTag(format);
            int channels = GetChannels(format);
            int rawBlockAlign = (int)((format >> 23) & 0xFF);

            switch (tag)
            {
                case TAG_PCM:
                    return rawBlockAlign;

                case TAG_XMA:
                    return channels * 2;

                case TAG_ADPCM:
                    return (rawBlockAlign + 22) * channels;

                case TAG_WMA:
                    int[] blockAlignTable =
                    {
                        929, 1487, 1280, 2230, 8917, 8192, 4459, 5945, 2304,
                        1536, 1485, 1008, 2731, 4096, 6827, 5462, 1280
                    };

                    int index = rawBlockAlign & 0x1F;
                    return index < blockAlignTable.Length ? blockAlignTable[index] : 0;

                default:
                    return rawBlockAlign;
            }
        }

        /// <summary>
        /// Computes bit depth from the packed format field.
        /// </summary>
        private static int GetBitsPerSample(uint format)
        {
            uint tag = GetFormatTag(format);
            int pcmBitFlag = (int)((format >> 31) & 0x1);

            switch (tag)
            {
                case TAG_PCM:
                    return pcmBitFlag == 1 ? 16 : 8;

                case TAG_ADPCM:
                    return 4;

                case TAG_WMA:
                case TAG_XMA:
                    return 16;

                default:
                    return 0;
            }
        }

        /// <summary>
        /// Computes average bytes per second from the packed format field.
        /// Used primarily for WAV metadata output.
        /// </summary>
        private static int GetAvgBytesPerSecond(uint format)
        {
            uint tag = GetFormatTag(format);
            int sampleRate = GetSampleRate(format);
            int blockAlign = GetBlockAlign(format);
            int channels = GetChannels(format);
            int rawBlockAlign = (int)((format >> 23) & 0xFF);

            switch (tag)
            {
                case TAG_PCM:
                    return sampleRate * blockAlign;

                case TAG_XMA:
                    return sampleRate * blockAlign;

                case TAG_ADPCM:
                    int samplesPerBlock = AdpcmSamplesPerBlock(channels, blockAlign);
                    return samplesPerBlock > 0 ? (blockAlign * sampleRate) / samplesPerBlock : 0;

                case TAG_WMA:
                    int[] avgTable = { 12000, 24000, 4000, 6000, 8000, 20000, 2500 };
                    int index = rawBlockAlign >> 5;
                    return index < avgTable.Length ? avgTable[index] : 0;

                default:
                    return 0;
            }
        }

        /// <summary>
        /// Extracts duration-in-samples from the packed flags/duration field.
        /// </summary>
        private static uint GetDurationSamples(uint flagsAndDuration)
        {
            return flagsAndDuration >> 4;
        }

        /// <summary>
        /// Calculates ADPCM samples-per-block for WAV metadata.
        /// </summary>
        private static int AdpcmSamplesPerBlock(int channels, int blockAlign)
        {
            if (channels <= 0 || blockAlign <= 0)
                return 0;

            return (blockAlign * 2 / channels) - 12;
        }
        #endregion

        /// <summary>
        /// Small model containers used while parsing bank metadata.
        /// </summary>
        #region Model Types

        /// <summary>
        /// Describes one XWB segment entry from the header.
        /// </summary>
        private sealed class Segment
        {
            public uint Offset;
            public uint Length;
        }

        /// <summary>
        /// Bank-level metadata read from the BANKDATA segment.
        /// </summary>
        private sealed class BankData
        {
            public uint Flags;
            public uint EntryCount;
            public string BankName = string.Empty;
            public uint EntryMetaDataElementSize;
            public uint EntryNameElementSize;
            public uint Alignment;
            public uint CompactFormat;
            public uint BuildTimeLow;
            public uint BuildTimeHigh;
        }

        /// <summary>
        /// Per-entry metadata read from the ENTRYMETADATA segment.
        /// </summary>
        private sealed class Entry
        {
            public uint FlagsAndDuration;
            public uint Format;
            public uint PlayOffset;
            public uint PlayLength;
            public uint LoopStart;
            public uint LoopLength;
        }
        #endregion
    }
}
