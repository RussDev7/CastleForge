/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using DNA.Security.Cryptography;
using DNA.IO.Storage;
using System.Text;
using System.IO;
using System;

namespace TexturePacks.UnusedConverters
{
    /// <summary>
    /// Preserved standalone-style CastleMiner Z Xbox 360 world converter.
    /// Intended for archival / one-off conversion use and not wired into the TexturePacks runtime.
    /// </summary>
    internal static class CMZ360ToPCWorldConverter
    {
        /// <summary>
        /// Helper entry points for manually converting Xbox 360-style world saves into PC-readable output folders.
        /// </summary>
        #region Converter Entry Points

        /// <summary>
        /// Converts worlds from the specified working directory.
        /// Expected layout:
        ///   &lt;workingDirectory&gt;\Worlds
        /// Writes output to:
        ///   &lt;workingDirectory&gt;\OutputWorlds
        ///
        /// Behavior preserved from the original utility:
        /// - Uses the provided owner token + "CMZ778" as the decryption key seed.
        /// - If the token length is less than 17, world.info files are rewritten after extraction.
        ///   This mirrors the original "gamertag vs SteamID64" decision path.
        /// </summary>
        public static void ConvertFromWorkingDirectory(string ownerGamerTagOrSteamId64, string workingDirectory = null)
        {
            workingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? Directory.GetCurrentDirectory()
                : workingDirectory;

            bool rewriteWorldInfos = !string.IsNullOrEmpty(ownerGamerTagOrSteamId64) && ownerGamerTagOrSteamId64.Length < 17;

            DecryptSaves(ownerGamerTagOrSteamId64, workingDirectory);

            if (rewriteWorldInfos)
            {
                SaveDevice device = new FileSystemSaveDevice(workingDirectory, null);
                UpdateSaves(device);
            }
        }

        /// <summary>
        /// Convenience wrapper that exposes success / failure without throwing to the caller.
        /// </summary>
        public static bool TryConvertFromWorkingDirectory(string ownerGamerTagOrSteamId64, string workingDirectory, out string error)
        {
            try
            {
                ConvertFromWorkingDirectory(ownerGamerTagOrSteamId64, workingDirectory);
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
        /// This is the main conversion logic preserved from the standalone world converter.
        /// </summary>
        #region Conversion Core

        /// <summary>
        /// Re-saves any extracted world.info files using the preserved mutation logic.
        /// </summary>
        private static void UpdateSaves(SaveDevice device)
        {
            foreach (WorldInfoRecord worldInfo in WorldInfoRecord.LoadWorldInfo(device))
            {
                Console.WriteLine("Saving new worldinfo");
                worldInfo.SaveToStorage(device);
            }
        }

        /// <summary>
        /// Decrypts world files from the Worlds folder and writes them to OutputWorlds.
        /// </summary>
        private static void DecryptSaves(string gamertag, string directory)
        {
            if (string.IsNullOrWhiteSpace(gamertag))
                throw new ArgumentException("A gamertag or SteamID64 is required.", nameof(gamertag));

            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("A working directory is required.", nameof(directory));

            string worldsDirectory = Path.Combine(directory, "Worlds");
            string outputDirectory = Path.Combine(directory, "OutputWorlds");

            if (!Directory.Exists(worldsDirectory))
                Directory.CreateDirectory(worldsDirectory);

            if (!Directory.Exists(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            byte[] data = new MD5HashProvider().Compute(Encoding.UTF8.GetBytes(gamertag + "CMZ778")).Data;
            SaveDevice encryptedDevice = new FileSystemSaveDevice(worldsDirectory, data);
            SaveDevice outputDevice = new FileSystemSaveDevice(outputDirectory, null);
            List<string> files = new List<string>();

            GetFiles(worldsDirectory, files);

            foreach (string file in files)
            {
                byte[] dataToSave;
                try
                {
                    Console.WriteLine("Loading " + file.Replace(directory, string.Empty));
                    dataToSave = encryptedDevice.LoadData(file);
                }
                catch
                {
                    Console.WriteLine("Failed to load " + file.Replace(directory, string.Empty));
                    continue;
                }

                string outputPath = file.Replace(worldsDirectory, outputDirectory);
                string outputFolder = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(outputFolder) && !Directory.Exists(outputFolder))
                    Directory.CreateDirectory(outputFolder);

                Console.WriteLine("saving " + outputPath.Replace(directory, string.Empty));
                outputDevice.Save(outputPath, dataToSave, false, false);
            }
        }

        /// <summary>
        /// Recursively gathers all files under the provided path.
        /// </summary>
        private static void GetFiles(string path, List<string> returnedFiles)
        {
            string[] directories = Directory.GetDirectories(path);
            for (int i = 0; i < directories.Length; i++)
                GetFiles(directories[i], returnedFiles);

            foreach (string file in Directory.GetFiles(path))
                returnedFiles.Add(file);
        }
        #endregion

        /// <summary>
        /// Preserved world.info helper model from the original converter, collapsed into this single file.
        /// </summary>
        #region Nested World Info Model

        /// <summary>
        /// Preserved world-info representation used by the converter when rewriting extracted metadata.
        /// </summary>
        private sealed class WorldInfoRecord
        {
            /// <summary>
            /// Public metadata preserved from the original converter structure.
            /// </summary>
            #region World Info Properties / Fields

            public int Version
            {
                get { return 5; }
            }

            public string SavePath
            {
                get { return _savePath; }
                set { _savePath = value; }
            }

            public string Name
            {
                get { return _name; }
                set { _name = value; }
            }

            public string OwnerGamerTag
            {
                get { return _ownerGamerTag; }
            }

            public string CreatorGamerTag
            {
                get { return _creatorGamerTag; }
            }

            public DateTime CreatedDate
            {
                get { return _createdDate; }
            }

            public DateTime LastPlayedDate
            {
                get { return _lastPlayedDate; }
                set { _lastPlayedDate = value; }
            }

            public int Seed
            {
                get { return _seed; }
            }

            public Guid WorldID
            {
                get { return _worldID; }
            }

            public byte[] Bytes;
            public static List<string> CorruptWorlds = new List<string>();

            private static readonly string BasePath = "OutputWorlds";
            private static readonly string FileName = "world.info";

            private string                _savePath;
            private readonly WorldTypeIDs _terrainVersion = WorldTypeIDs.CastleMinerZ;
            private string                _name = "World";
            private readonly string       _ownerGamerTag;
            private readonly string       _creatorGamerTag;
            private readonly DateTime     _createdDate;
            private DateTime              _lastPlayedDate;
            private readonly int          _seed;
            private readonly Guid         _worldID;

            public bool   InfiniteResourceMode;
            public int    HellBossesSpawned;
            public int    MaxHellBossSpawns;
            public string ServerMessage  = string.Empty;
            public string ServerPassword = string.Empty;
            #endregion

            /// <summary>
            /// Creates a preserved world-info record with the original default server message.
            /// </summary>
            #region Construction / Discovery

            private WorldInfoRecord()
            {
                ServerMessage = string.Empty;
            }

            /// <summary>
            /// Loads all world.info entries from the converter's OutputWorlds folder.
            /// </summary>
            public static WorldInfoRecord[] LoadWorldInfo(SaveDevice device)
            {
                try
                {
                    CorruptWorlds.Clear();
                    if (!device.DirectoryExists(BasePath))
                        return new WorldInfoRecord[0];

                    List<WorldInfoRecord> worlds = new List<WorldInfoRecord>();
                    foreach (string folder in device.GetDirectories(BasePath))
                    {
                        WorldInfoRecord worldInfo = null;
                        try
                        {
                            worldInfo = LoadFromStorage(folder, device);
                        }
                        catch
                        {
                            worldInfo = null;
                            CorruptWorlds.Add(folder);
                        }

                        if (worldInfo != null)
                            worlds.Add(worldInfo);
                    }

                    return worlds.ToArray();
                }
                catch
                {
                    return new WorldInfoRecord[0];
                }
            }

            /// <summary>
            /// Loads one world.info payload from storage.
            /// </summary>
            private static WorldInfoRecord LoadFromStorage(string folder, SaveDevice saveDevice)
            {
                WorldInfoRecord info = new WorldInfoRecord();
                saveDevice.Load(Path.Combine(folder, FileName), delegate(Stream stream)
                {
                    info.Load(stream);
                    info._savePath = folder;
                });
                return info;
            }
            #endregion

            /// <summary>
            /// This is the preserved read / write logic for world.info mutation.
            /// </summary>
            #region Save / Load Logic

            /// <summary>
            /// Saves the preserved world.info mutation back to storage.
            /// </summary>
            public void SaveToStorage(SaveDevice saveDevice)
            {
                try
                {
                    if (!saveDevice.DirectoryExists(SavePath))
                        saveDevice.CreateDirectory(SavePath);

                    string fileName = Path.Combine(SavePath, FileName);
                    saveDevice.Save(fileName, false, false, delegate(Stream stream)
                    {
                        Console.WriteLine("SaveDevice");
                        BinaryWriter binaryWriter = new BinaryWriter(stream);
                        Save(binaryWriter);
                        binaryWriter.Flush();
                    });
                }
                catch
                {
                }
            }

            /// <summary>
            /// Writes a modified world.info payload.
            /// The preserved behavior forces the first byte block to begin with value 2,
            /// then appends ServerMessage and ServerPassword.
            /// </summary>
            public void Save(BinaryWriter writer)
            {
                byte[] prefix = new byte[4];
                prefix[0] = 2;

                for (int i = 0; i < prefix.Length; i++)
                {
                    Console.WriteLine("Saving new worldinfo " + i);
                    writer.Write(prefix[i]);
                }

                if (Bytes == null)
                    Bytes = new byte[0];

                for (int i = 4; i < Bytes.Length; i++)
                {
                    Console.WriteLine("Saving new worldinfo " + i);
                    writer.Write(Bytes[i]);
                }

                writer.Write(ServerMessage);
                writer.Write(ServerPassword);
            }

            /// <summary>
            /// Reads the full world.info stream into memory.
            /// </summary>
            private void Load(Stream stream)
            {
                Bytes = ReadAllBytes(stream);
            }

            /// <summary>
            /// Reads all bytes from the given stream.
            /// </summary>
            private static byte[] ReadAllBytes(Stream stream)
            {
                using (MemoryStream memoryStream = new MemoryStream())
                {
                    stream.CopyTo(memoryStream);
                    return memoryStream.ToArray();
                }
            }
            #endregion

            /// <summary>
            /// Preserved enums from the original converter.
            /// </summary>
            #region Enums

            private enum WorldInfoVersion
            {
                Initial = 1,
                Doors,
                Spawners,
                HellBosses,
                CurrentVersion
            }

            public enum WorldTypeIDs
            {
                CastleMinerZ = 1
            }
            #endregion
        }
        #endregion
    }
}
