/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System;

namespace ModLoader
{
    /// <summary>
    /// Applied to a <see cref="ModBase"/>-derived class to declare
    /// other mods that must be loaded before this one.
    /// </summary>
    /// <remarks>
    /// Usage:
    /// <code>
    /// [RequiredDependencies("ModLoaderExtensions", "AnotherMod")]
    /// public class MyPlugin : ModBase { ... }
    /// </code>
    /// If omitted (or supplied with no names), this mod has no prerequisites.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class RequiredDependencies : Attribute
    {
        /// <summary>
        /// The names of other mods (i.e. the <see cref="ModBase.Name"/> values)
        /// that this mod requires.  These must appear in the load folder and
        /// be successfully started before this mod will be initialized.
        /// </summary>
        public string[] Mods { get; }

        /// <summary>
        /// Creates a new <see cref="RequiredDependencies"/> attribute.
        /// </summary>
        /// <param name="mods">
        /// Zero or more mod names.  If none provided, the array will be empty
        /// and no dependencies are enforced.
        /// </param>
        public RequiredDependencies(params string[] mods)
        {
            Mods = mods ?? Array.Empty<string>();
        }
    }
}