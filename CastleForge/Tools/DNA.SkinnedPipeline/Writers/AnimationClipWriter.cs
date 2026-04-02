/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework.Content.Pipeline.Serialization.Compiler;
using Microsoft.Xna.Framework.Content.Pipeline;
using Microsoft.Xna.Framework;
using DNA.Drawing.Animation;
using System.Reflection;
using System;

namespace CMZ.ContentPipeline
{
    /// <summary>
    /// =========================================================================================
    /// AnimationClipWriter
    /// =========================================================================================
    ///
    /// Purpose
    /// -------
    /// Writes DNA.Drawing.Animation.AnimationClip into XNB using the *exact* binary layout expected
    /// by the game's runtime reader:
    ///     DNA.Drawing.Animation.AnimationClip+Reader
    ///
    /// Why this exists
    /// ---------------
    /// If a custom writer is not supplied, XNA may fall back to ReflectiveReader<AnimationClip>,
    /// which can conflict with the runtime's already-registered AnimationClip.Reader (and/or
    /// produce a different layout than the game expects).
    ///
    /// Layout Contract (high level)
    /// ----------------------------
    /// The writer emits fields in the order the runtime reader consumes them:
    ///   1) Name (string)
    ///   2) _animationFrameRate (int)         [private field via reflection]
    ///   3) Duration ticks (long)             [TimeSpan.FromTicks]
    ///   4) BoneCount (int)
    ///   5) Per-bone arrays:
    ///        - Positions:  (int frameCount) + frameCount * Vector3
    ///        - Rotations:  (int frameCount) + frameCount * Quaternion
    ///        - Scales:     (int frameCount) + frameCount * Vector3
    ///
    /// Notes
    /// -----
    /// - Null clips are serialized as a safe, empty clip (instead of throwing).
    /// - Array validation is defensive: missing rotations/scales become empty arrays.
    /// - The runtime reader string is forced via GetRuntimeReader() to guarantee XNB manifest
    ///   points at the game's reader type.
    ///
    /// =========================================================================================
    /// </summary>
    [ContentTypeWriter]
    public sealed class AnimationClipWriter : ContentTypeWriter<AnimationClip>
    {
        #region ContentTypeWriter Overrides

        /// <summary>
        /// Serialize an AnimationClip into XNB in the exact field order expected by the runtime reader.
        /// </summary>
        protected override void Write(ContentWriter output, AnimationClip value)
        {
            if (value == null)
            {
                // Runtime reader expects a real object; write a safe empty clip.
                // (If you prefer to throw, change this to throw new ArgumentNullException(nameof(value)).)
                WriteEmpty(output);
                return;
            }

            // Name (reader: ReadString).
            output.Write(value.Name ?? "");

            // _animationFrameRate (reader: ReadInt32).
            // Private field, so we grab it with reflection.
            int afr = GetInt(value, "_animationFrameRate", 30);
            output.Write(afr);

            // Duration ticks (reader: ReadInt64 -> TimeSpan.FromTicks)
            output.Write(value.Duration.Ticks);

            // BoneCount (reader: ReadInt32).
            // BoneCount property returns _positions.Length, but we still validate the arrays.
            var positions = GetField<Vector3[][]>(value, "_positions");
            var rotations = GetField<Quaternion[][]>(value, "_rotations");
            var scales = GetField<Vector3[][]>(value, "_scales");

            int boneCount = positions?.Length ?? 0;
            output.Write(boneCount);

            for (int i = 0; i < boneCount; i++)
            {
                // Positions: Frames + Vector3s.
                var posFrames = positions[i] ?? Array.Empty<Vector3>();
                output.Write(posFrames.Length);
                for (int j = 0; j < posFrames.Length; j++)
                    WriteVector3(output, posFrames[j]);

                // Rotations: frames + Quaternions.
                var rotFrames = (rotations != null && i < rotations.Length && rotations[i] != null)
                                ? rotations[i]
                                : Array.Empty<Quaternion>();
                output.Write(rotFrames.Length);
                for (int j = 0; j < rotFrames.Length; j++)
                    WriteQuaternion(output, rotFrames[j]);

                // Scales: Frames + Vector3s.
                var sclFrames = (scales != null && i < scales.Length && scales[i] != null)
                                ? scales[i]
                                : Array.Empty<Vector3>();
                output.Write(sclFrames.Length);
                for (int j = 0; j < sclFrames.Length; j++)
                    WriteVector3(output, sclFrames[j]);
            }
        }

        /// <summary>
        /// Forces the XNB manifest to bind to the game's existing runtime reader type.
        /// </summary>
        public override string GetRuntimeReader(TargetPlatform targetPlatform)
        {
            // Forces the XNB manifest to use the game's existing reader:
            // DNA.Drawing.Animation.AnimationClip+Reader, DNA.Common, ...
            return typeof(AnimationClip.Reader).AssemblyQualifiedName;
        }

        /// <summary>
        /// Declares the runtime type that this writer produces.
        /// </summary>
        public override string GetRuntimeType(TargetPlatform targetPlatform)
        {
            return typeof(AnimationClip).AssemblyQualifiedName;
        }
        #endregion

        #region Serialization Helpers (Binary Layout)

        /// <summary>
        /// Writes a minimal, safe "empty" clip that the runtime reader can load without null checks.
        /// </summary>
        private static void WriteEmpty(ContentWriter output)
        {
            output.Write("");
            output.Write(30);
            output.Write(0L); // Ticks.
            output.Write(0);  // BoneCount.
        }

        /// <summary>
        /// Writes a Vector3 as 3 floats (X, Y, Z) matching typical BinaryReader.ReadVector3 patterns.
        /// </summary>
        private static void WriteVector3(ContentWriter output, Vector3 v)
        {
            // Matches typical BinaryReader.ReadVector3 extension: 3 floats.
            output.Write(v.X);
            output.Write(v.Y);
            output.Write(v.Z);
        }

        /// <summary>
        /// Writes a Quaternion as 4 floats (X, Y, Z, W) matching typical BinaryReader.ReadQuaternion patterns.
        /// </summary>
        private static void WriteQuaternion(ContentWriter output, Quaternion q)
        {
            // Matches typical BinaryReader.ReadQuaternion extension: 4 floats.
            output.Write(q.X);
            output.Write(q.Y);
            output.Write(q.Z);
            output.Write(q.W);
        }
        #endregion

        #region Reflection Helpers (Private Field Access)

        // NOTE:
        // These helpers are intentionally defensive (try/catch + fallbacks) because content pipeline
        // types can vary depending on referenced DNA assemblies, and we want failures to degrade
        // gracefully rather than breaking builds.

        private static T GetField<T>(object obj, string fieldName) where T : class
        {
            try
            {
                var f = obj.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                return f?.GetValue(obj) as T;
            }
            catch { return null; }
        }

        private static int GetInt(object obj, string fieldName, int fallback)
        {
            try
            {
                var f = obj.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                if (f != null && f.FieldType == typeof(int))
                    return (int)f.GetValue(obj);
            }
            catch { }
            return fallback;
        }
        #endregion
    }
}