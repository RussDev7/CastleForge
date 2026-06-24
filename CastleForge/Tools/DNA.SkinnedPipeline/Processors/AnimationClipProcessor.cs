/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework.Content.Pipeline.Graphics;
using Microsoft.Xna.Framework.Content.Pipeline;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using DNA.Drawing.Animation;
using System.ComponentModel;
using System.Linq;
using System;

namespace CMZ.ContentPipeline
{
    /// <summary>
    /// =========================================================================================
    /// AnimationClipProcessor
    /// =========================================================================================
    ///
    /// Purpose
    /// -------
    /// XNA Content Pipeline processor for standalone CMZ/DNA avatar animation clips.
    ///
    /// Pipeline Responsibilities
    /// -------------------------
    /// 1) Locate an FBX animation take:
    ///    - Uses SourceClipName when supplied
    ///    - Falls back to the first available FBX animation take
    ///
    /// 2) Build per-bone transform arrays:
    ///    - Preserves imported node/bone traversal order
    ///    - Samples position, rotation, and scale at the requested FrameRate
    ///    - Falls back to each node's bind/local transform when no animation channel exists
    ///
    /// 3) Create a standalone DNA.Drawing.Animation.AnimationClip:
    ///    - Uses ClipName when supplied
    ///    - Otherwise uses the selected source take name, then the root asset name
    ///    - Optionally reduces constant transform keys
    ///
    /// Intended Authoring Workflow
    /// ---------------------------
    /// - Import the exported CastleForge/CMZ player reference model into Blender.
    /// - Keep the armature bone names and hierarchy intact.
    /// - Export a single FBX animation.
    /// - Build it with FbxToXnb using this processor.
    ///
    /// Runtime Expectations
    /// --------------------
    /// The generated XNB is loaded by WeaponAddons as an AnimationClip and registered with
    /// AvatarAnimationManager. WeaponAddons then remaps vanilla animation names to this custom clip
    /// when the matching weapon item is held.
    ///
    /// Notes / Limitations
    /// -------------------
    /// - This processor does not edit or decompile vanilla animation XNBs.
    /// - The FBX must contain animation channels that match the exported reference skeleton names
    ///   and hierarchy as closely as possible.
    /// - AnimationClip stores per-bone arrays by index, so the reference armature/node order matters.
    /// - Missing channels are allowed and use the node's fallback transform.
    ///
    /// =========================================================================================
    /// </summary>
    [ContentProcessor(DisplayName = "AnimationClipProcessor")]
    public sealed class AnimationClipProcessor : ContentProcessor<NodeContent, AnimationClip>
    {
        #region Processor Settings

        /// <summary>
        /// Output sample rate. CastleMiner Z vanilla avatar clips are normally authored at 30 FPS.
        /// </summary>
        [DefaultValue(30)]
        public int FrameRate { get; set; } = 30;

        /// <summary>
        /// Optional output clip name. Blank uses the selected FBX take name, then the asset/root name.
        /// </summary>
        [DefaultValue("")]
        public string ClipName { get; set; } = "";

        /// <summary>
        /// Optional source take name from the FBX. Blank uses the first available take.
        /// </summary>
        [DefaultValue("")]
        public string SourceClipName { get; set; } = "";

        /// <summary>
        /// Reduces constant transform channels after building the clip.
        /// </summary>
        [DefaultValue(true)]
        public bool ReduceKeys { get; set; } = true;

        #endregion

        #region Content Pipeline Entry Point

        /// <summary>
        /// Main processor entry point invoked by the XNA content pipeline.
        /// </summary>
        public override AnimationClip Process(NodeContent input, ContentProcessorContext context)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            int frameRate = Math.Max(1, FrameRate);

            AnimationContent sourceClip = FindAnimation(input, SourceClipName, out string sourceName) ?? throw new InvalidContentException("No FBX animation takes were found. In Blender, make sure the exported FBX includes animation data.");
            var bones = CollectBones(input).ToList();
            if (bones.Count == 0)
                throw new InvalidContentException("No FBX nodes/bones were found to map the animation onto.");

            TimeSpan duration = GetDuration(sourceClip);
            if (duration <= TimeSpan.Zero)
                duration = TimeSpan.FromSeconds(1.0 / frameRate);

            int frameCount = Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds * frameRate) + 1);

            var channelLookup = BuildChannelLookup(sourceClip);
            var positions = new Vector3[bones.Count][];
            var rotations = new Quaternion[bones.Count][];
            var scales = new Vector3[bones.Count][];

            #region Sample Bone Transform Channels

            // AnimationClip stores transform data as parallel arrays indexed by bone/node order.
            // Keep this order aligned with the exported player reference armature.
            for (int boneIndex = 0; boneIndex < bones.Count; boneIndex++)
            {
                NodeContent bone = bones[boneIndex];

                positions[boneIndex] = new Vector3[frameCount];
                rotations[boneIndex] = new Quaternion[frameCount];
                scales[boneIndex] = new Vector3[frameCount];

                AnimationChannel channel = FindChannelForBone(channelLookup, bone);
                Matrix fallback = bone.Transform;

                for (int frame = 0; frame < frameCount; frame++)
                {
                    TimeSpan t = TimeSpan.FromSeconds(frame / (double)frameRate);
                    if (t > duration)
                        t = duration;

                    Matrix transform = channel != null
                        ? SampleChannel(channel, t, fallback)
                        : fallback;

                    if (!transform.Decompose(out Vector3 s, out Quaternion r, out Vector3 p))
                    {
                        s = Vector3.One;
                        r = Quaternion.Identity;
                        p = Vector3.Zero;
                    }

                    positions[boneIndex][frame] = p;
                    rotations[boneIndex][frame] = r;
                    scales[boneIndex][frame] = s;
                }
            }
            #endregion

            #region Build AnimationClip

            string outName = !string.IsNullOrWhiteSpace(ClipName)
                ? ClipName.Trim()
                : (!string.IsNullOrWhiteSpace(sourceName) ? sourceName : (input.Name ?? "Animation"));

            var clip = new AnimationClip(outName, frameRate, duration, positions, rotations, scales);

            if (ReduceKeys)
                clip.ReduceKeys();

            #endregion

            #region Content Pipeline Logging

            if (context != null && context.Logger != null)
            {
                context.Logger.LogImportantMessage(
                    "AnimationClipProcessor: built \"{0}\" from \"{1}\" with {2} bone(s), {3} frame(s), {4} FPS.",
                    outName,
                    sourceName,
                    bones.Count,
                    frameCount,
                    frameRate);
            }
            #endregion

            return clip;
        }
        #endregion

        #region Source Animation Selection

        /// <summary>
        /// Finds the requested FBX animation take, or returns the first available take when no name
        /// is supplied.
        /// </summary>
        private static AnimationContent FindAnimation(NodeContent root, string requestedName, out string sourceName)
        {
            sourceName = null;

            foreach (var node in EnumerateNodes(root))
            {
                if (node == null || node.Animations == null || node.Animations.Count == 0)
                    continue;

                if (!string.IsNullOrWhiteSpace(requestedName))
                {
                    foreach (var pair in node.Animations)
                    {
                        if (string.Equals(pair.Key, requestedName, StringComparison.OrdinalIgnoreCase))
                        {
                            sourceName = pair.Key;
                            return pair.Value;
                        }
                    }
                }

                foreach (var pair in node.Animations)
                {
                    sourceName = pair.Key;
                    return pair.Value;
                }
            }

            return null;
        }

        /// <summary>
        /// Determines the clip duration from the animation take and its keyframes.
        /// </summary>
        private static TimeSpan GetDuration(AnimationContent clip)
        {
            if (clip == null)
                return TimeSpan.Zero;

            TimeSpan duration = TimeSpan.Zero;

            try
            {
                if (clip.Duration > duration)
                    duration = clip.Duration;
            }
            catch { }

            try
            {
                foreach (var pair in clip.Channels)
                {
                    var channel = pair.Value;
                    if (channel == null)
                        continue;

                    foreach (var key in channel)
                    {
                        if (key.Time > duration)
                            duration = key.Time;
                    }
                }
            }
            catch { }

            return duration;
        }
        #endregion

        #region Channel Lookup / Bone Mapping

        /// <summary>
        /// Builds a tolerant channel lookup table so channels can be matched by full path, normalized
        /// path, or leaf bone name.
        /// </summary>
        private static Dictionary<string, AnimationChannel> BuildChannelLookup(AnimationContent clip)
        {
            var lookup = new Dictionary<string, AnimationChannel>(StringComparer.OrdinalIgnoreCase);

            if (clip == null || clip.Channels == null)
                return lookup;

            foreach (var pair in clip.Channels)
            {
                string key = pair.Key;
                var channel = pair.Value;
                if (string.IsNullOrWhiteSpace(key) || channel == null)
                    continue;

                AddLookup(lookup, key, channel);

                string normalized = key.Replace('/', '\\');
                AddLookup(lookup, normalized, channel);

                int slash = normalized.LastIndexOf('\\');
                if (slash >= 0 && slash + 1 < normalized.Length)
                    AddLookup(lookup, normalized.Substring(slash + 1), channel);
            }

            return lookup;
        }

        /// <summary>
        /// Adds a channel alias only once. First match wins, which keeps the source FBX channel order
        /// stable when duplicate aliases exist.
        /// </summary>
        private static void AddLookup(Dictionary<string, AnimationChannel> lookup, string key, AnimationChannel channel)
        {
            if (string.IsNullOrWhiteSpace(key) || channel == null)
                return;

            if (!lookup.ContainsKey(key))
                lookup.Add(key, channel);
        }

        /// <summary>
        /// Finds the animation channel that best matches the supplied imported node/bone.
        /// </summary>
        private static AnimationChannel FindChannelForBone(Dictionary<string, AnimationChannel> lookup, NodeContent bone)
        {
            if (lookup == null || bone == null)
                return null;

            string name = bone.Name ?? "";
            if (!string.IsNullOrWhiteSpace(name) && lookup.TryGetValue(name, out AnimationChannel channel))
                return channel;

            string fullPath = BuildNodePath(bone);
            if (!string.IsNullOrWhiteSpace(fullPath) && lookup.TryGetValue(fullPath, out channel))
                return channel;

            return null;
        }

        /// <summary>
        /// Builds a backslash-separated path from the imported root node to the supplied node.
        /// </summary>
        private static string BuildNodePath(NodeContent node)
        {
            if (node == null)
                return null;

            var parts = new List<string>();
            for (NodeContent cur = node; cur != null; cur = cur.Parent)
            {
                if (!string.IsNullOrWhiteSpace(cur.Name))
                    parts.Add(cur.Name);
            }

            parts.Reverse();
            return string.Join("\\", parts.ToArray());
        }

        /// <summary>
        /// Collects imported nodes/bones in traversal order for AnimationClip array indexing.
        /// </summary>
        private static IEnumerable<NodeContent> CollectBones(NodeContent root)
        {
            // Preserve FBX/importer hierarchy order. This is important because AnimationClip stores
            // per-bone arrays by index, not by name. Use the exported reference model/armature as the
            // source so this order stays aligned with the game avatar skeleton.
            foreach (var node in EnumerateNodes(root))
            {
                if (node != null && !string.IsNullOrWhiteSpace(node.Name))
                    yield return node;
            }
        }

        /// <summary>
        /// Enumerates the imported node tree in depth-first order.
        /// </summary>
        private static IEnumerable<NodeContent> EnumerateNodes(NodeContent node)
        {
            if (node == null)
                yield break;

            yield return node;

            if (node.Children == null)
                yield break;

            foreach (var child in node.Children)
            {
                foreach (var n in EnumerateNodes(child))
                    yield return n;
            }
        }
        #endregion

        #region Transform Sampling Helpers

        /// <summary>
        /// Samples an animation channel at the requested time, interpolating between adjacent
        /// keyframes when needed.
        /// </summary>
        private static Matrix SampleChannel(AnimationChannel channel, TimeSpan time, Matrix fallback)
        {
            if (channel == null || channel.Count == 0)
                return fallback;

            if (time <= channel[0].Time)
                return channel[0].Transform;

            int last = channel.Count - 1;
            if (time >= channel[last].Time)
                return channel[last].Transform;

            AnimationKeyframe previous = channel[0];
            AnimationKeyframe next = channel[last];

            for (int i = 1; i < channel.Count; i++)
            {
                if (channel[i].Time >= time)
                {
                    previous = channel[i - 1];
                    next = channel[i];
                    break;
                }
            }

            double spanTicks = (next.Time - previous.Time).Ticks;
            float amount = spanTicks <= 0.0
                ? 0f
                : (float)((time - previous.Time).Ticks / spanTicks);

            return LerpMatrix(previous.Transform, next.Transform, amount);
        }

        /// <summary>
        /// Interpolates two transform matrices by decomposing them into scale, rotation, and position.
        /// </summary>
        private static Matrix LerpMatrix(Matrix a, Matrix b, float amount)
        {

            if (!a.Decompose(out Vector3 scaleA, out Quaternion rotA, out Vector3 posA))
                return a;

            if (!b.Decompose(out Vector3 scaleB, out Quaternion rotB, out Vector3 posB))
                return a;

            Vector3 scale = Vector3.Lerp(scaleA, scaleB, amount);
            Quaternion rot = Quaternion.Slerp(rotA, rotB, amount);
            Vector3 pos = Vector3.Lerp(posA, posB, amount);

            return Matrix.CreateScale(scale) *
                   Matrix.CreateFromQuaternion(rot) *
                   Matrix.CreateTranslation(pos);
        }
        #endregion
    }
}