/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System;

namespace ModLoader
{
    /// <summary>
    /// Declares the relative load priority for a mod. Higher numbers load earlier.
    /// </summary>
    /// <remarks>
    /// The loader should treat the absence of this attribute as <see cref="Priority.Normal"/>.
    /// Priority only influences ordering among mods whose dependencies are already satisfied.
    /// Dependencies always take precedence: A high-priority mod will still wait until its
    /// [RequiredDependencies] are loaded.
    /// </remarks>
    /// <example>
    /// [Priority(Priority.High)]
    /// public sealed class MyCoolMod : ModBase { /* ... */ }
    /// </example>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class PriorityAttribute : Attribute
    {
        /// <summary>
        /// Numeric priority value used by the loader to order mods.
        /// Higher values load earlier (e.g., <see cref="Priority.First"/>).
        /// </summary>
        public int Value { get; }

        /// <summary>
        /// Create a new priority tag for a mod class.
        /// </summary>
        /// <param name="value">
        /// The numeric priority to assign. See <see cref="Priority"/> for recommended bands.
        /// Values outside those bands are allowed but discouraged for consistency.
        /// </param>
        public PriorityAttribute(int value) => Value = value;
    }

    /// <summary>
    /// Recommended priority bands for mod loading.
    /// </summary>
    /// <remarks>
    /// These are conventions to keep ordering predictable and readable in logs.
    /// The loader should sort by <see cref="PriorityAttribute.Value"/> descending, then apply a
    /// deterministic tiebreaker (e.g., type name) for equal priorities.
    /// </remarks>
    public static class Priority
    {
        // Lowest priority. Loads last.
        public const int Last             = 0;

        // De-emphasized bands. Use for cosmetic or optional mods that can safely load late.
        public const int VeryLow          = 100;
        public const int Low              = 200;
        public const int LowerThanNormal  = 300;

        // Default priority when no [Priority] attribute is present.
        public const int Normal           = 400;

        // Elevated bands. Use sparingly for foundational mods that benefit from earlier Start().
        public const int HigherThanNormal = 500;
        public const int High             = 600;
        public const int VeryHigh         = 700;

        // Highest priority. Loads first (subject to dependency resolution).
        public const int First            = 800;

        // Bootstrap priority. Reserved for mod-loader api. Loads before everything else.
        public const int Bootstrap        = 1000;
    }
}