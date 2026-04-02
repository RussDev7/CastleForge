/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework;
using DNA.Input;
using System;

namespace ModLoader
{
    /// <summary>
    /// Base class for all mods loaded by the ModLoader system.
    /// Provides common metadata and lifecycle hooks.
    /// </summary>
    public abstract class ModBase
    {
        public string  Name    { get; } // Human-readable name of the mod, used for logging and display.
        public Version Version { get; } // Version of the mod, for compatibility and update checks.

        // Constructs a new mod with the specified name and version.
        protected ModBase(string name, Version version)
        {
            Name    = name;
            Version = version;
        }

        // Called once when the mod is first loaded. Perform initialization here,
        // such as hooking events, loading resources, or registering commands.
        public abstract void Start();

        // Called each game update tick. Use this for per-frame or periodic logic.
        public abstract void Tick(InputManager inputManager, GameTime gameTime);
    }
}
