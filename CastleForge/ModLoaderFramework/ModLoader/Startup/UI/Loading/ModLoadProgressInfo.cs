/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

namespace ModLoader
{
    /// <summary>
    /// Snapshot of mod-load progress used by the temporary loading window.
    /// </summary>
    public sealed class ModLoadProgressInfo
    {
        public string Phase        { get; set; }
        public string CurrentItem  { get; set; }

        public int Total           { get; set; }
        public int Processed       { get; set; }
        public int Loaded          { get; set; }
        public int Failed          { get; set; }

        public bool IsIndeterminate { get; set; }
    }
}