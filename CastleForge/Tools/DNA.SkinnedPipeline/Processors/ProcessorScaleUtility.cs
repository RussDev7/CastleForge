/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework.Content.Pipeline.Processors;
using Microsoft.Xna.Framework.Content.Pipeline.Graphics;
using Microsoft.Xna.Framework.Content.Pipeline;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System.Globalization;
using System.IO;
using System;

namespace CMZ.ContentPipeline
{
    /// <summary>
    /// Shared helpers for CMZ/DNA model processors.
    ///
    /// Why this exists:
    /// - The stock XNA ModelProcessor.Scale value is enough for visible mesh geometry.
    /// - Pure socket/helper nodes such as BarrelTip can come through Blender/FBX under a
    ///   scaled parent/root node.
    /// - CastleMiner Z does not calculate BarrelTip as a normal child bone at runtime for
    ///   gun origins. GunEntity reads Model.Bones["BarrelTip"].Transform directly for the
    ///   shot origin and Skeleton["BarrelTip"].Transform directly for the muzzle flash.
    /// - Therefore BarrelTip must be stored as a clean model-root-space transform in the
    ///   rebuilt XNB, not as a child-space transform that depends on a Blender export parent.
    /// </summary>
    internal static class ProcessorScaleUtility
    {
        #region Defaults / Settings

        public const string DefaultSocketNodeNames = "BarrelTip,Flame";

        /// <summary>
        /// These nodes are baked to model-root space before the stock ModelProcessor runs.
        /// BarrelTip is the important one for guns. Flame is intentionally not included by
        /// default to avoid changing torch/cloud behavior unless explicitly requested.
        /// </summary>
        public const string DefaultSocketBakeToModelRootNames = "BarrelTip";

        /// <summary>
        /// Optional socket orientation correction. Disabled by default because the correct
        /// fix for this round-trip is root-space baking plus scale compensation, not a
        /// hardcoded rotation flip.
        /// </summary>
        public const string DefaultSocketRotationCorrectionNames = "BarrelTip";
        public const string DefaultSocketRotationCorrectionAxis = "Y";
        public const float DefaultSocketRotationCorrectionDegrees = 180f;
        public const float DefaultSocketBasisScale = 0.01f;
        public const string DefaultSocketBasisTransform = "BlenderGlbRoundTripForward";
        public const string DefaultAuthoringLocation = "0,0,0";
        public const string DefaultAuthoringRotation = "";
        public const string DefaultAuthoringRotationQuaternion = "1,0,0,0";
        public const string DefaultRigidMeshRestoreFile = "";
        public const float DefaultAuthoringLocationScale = 100f;
        public const float DefaultRigidMeshRestoreUnitScale = 100f;

        /// <summary>
        /// A non-positive SocketTranslationScale means: use the same value as socketBasisScale.
        /// For TexturePacks FbxComp=10 round-trips, this should normally stay automatic, so
        /// BarrelTip translation is scaled by the same 0.01/FbxComp value as the mesh.
        /// </summary>
        public const float AutoSocketTranslationScale = 0f;

        #endregion

        #region Authoring Transform

        /// <summary>
        /// Removes the optional TexturePacks authoring transform from imported root nodes before ModelProcessor runs.
        /// </summary>
        public static void ApplyInverseAuthoringTransform(
            NodeContent input,
            string authoringLocation,
            string authoringRotation,
            string authoringRotationQuaternion,
            float authoringLocationScale,
            ContentProcessorContext context,
            bool debugLog)
        {
            if (input == null)
                return;

            Vector3 location = BlenderLocationToGltf(ParseVector3(authoringLocation, Vector3.Zero));
            Quaternion rotation = BlenderQuaternionToGltf(ParseAuthoringRotation(authoringRotation, authoringRotationQuaternion, Quaternion.Identity));
            float effectiveAuthoringLocationScale = (authoringLocationScale > 0f && !float.IsNaN(authoringLocationScale) && !float.IsInfinity(authoringLocationScale))
                ? authoringLocationScale
                : DefaultAuthoringLocationScale;
            location *= effectiveAuthoringLocationScale;

            Matrix authoring = CreateAuthoringTransformMatrix(
                authoringLocation,
                authoringRotation,
                authoringRotationQuaternion,
                authoringLocationScale);

            if (IsNearlyIdentity(authoring))
                return;

            Matrix.Invert(ref authoring, out Matrix inverse);

            var roots = new List<NodeContent>();
            if (!string.IsNullOrWhiteSpace(input.Name) &&
                input.Name.Trim().Equals("RootNode", StringComparison.OrdinalIgnoreCase))
            {
                roots.Add(input);
            }
            else
            {
                CollectMatchingNodes(input, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "RootNode" }, roots);
            }

            if (roots.Count == 0)
                roots.Add(input);

            var seen = new HashSet<NodeContent>();
            foreach (var root in roots)
            {
                if (root == null || !seen.Add(root))
                    continue;

                root.Transform *= inverse;

                if (debugLog && context != null)
                {
                    try
                    {
                        context.Logger.LogMessage(
                            "[CMZ] Removed authoring transform from '{0}'. Location=({1}, {2}, {3}), RotationWXYZ=({4}, {5}, {6}, {7}), RotationInput='{8}', LocationScale={9}.",
                            root.Name ?? "<unnamed>",
                            location.X.ToString(CultureInfo.InvariantCulture),
                            location.Y.ToString(CultureInfo.InvariantCulture),
                            location.Z.ToString(CultureInfo.InvariantCulture),
                            rotation.W.ToString(CultureInfo.InvariantCulture),
                            rotation.X.ToString(CultureInfo.InvariantCulture),
                            rotation.Y.ToString(CultureInfo.InvariantCulture),
                            rotation.Z.ToString(CultureInfo.InvariantCulture),
                            !string.IsNullOrWhiteSpace(authoringRotation) ? authoringRotation : authoringRotationQuaternion,
                            effectiveAuthoringLocationScale.ToString(CultureInfo.InvariantCulture));
                    }
                    catch { }
                }
            }
        }
        #endregion

        #region Rigid Mesh Rotation Restore

        /// <summary>
        /// Restores original local node transforms saved by TexturePacks when
        /// NormalizeRigidMeshRotation=true was used for Blender-friendly authoring.
        /// </summary>
        /// <remarks>
        /// New sidecars contain both original and authoring transforms. The processor computes a
        /// delta between those transforms and the imported FBX node, preserving user edits while
        /// undoing the exporter-only normalization step. Older rotation-only sidecars continue to
        /// use the legacy fallback path.
        /// </remarks>
        public static int ApplyRigidMeshRotationRestore(
            NodeContent input,
            string restoreFile,
            float restoreUnitScale,
            string authoringLocation,
            string authoringRotation,
            string authoringRotationQuaternion,
            float authoringLocationScale,
            ContentProcessorContext context,
            bool debugLog)
        {
            if (input == null || string.IsNullOrWhiteSpace(restoreFile))
                return 0;

            if (restoreUnitScale <= 0f ||
                float.IsNaN(restoreUnitScale) ||
                float.IsInfinity(restoreUnitScale))
            {
                restoreUnitScale = DefaultRigidMeshRestoreUnitScale;
            }

            var transforms = ReadRigidNodeTransformRestoreFile(restoreFile, restoreUnitScale);
            var rotations = ReadRigidMeshRotationRestoreFile(restoreFile);

            if (transforms.Count == 0 && rotations.Count == 0)
                return 0;

            Matrix rootAuthoringImporter = CreateAuthoringTransformMatrix(
                authoringLocation,
                authoringRotation,
                authoringRotationQuaternion,
                authoringLocationScale);
            bool hasRootAuthoring = !IsNearlyIdentity(rootAuthoringImporter);

            var allNodes = new List<NodeContent>();
            CollectAllNodes(input, allNodes);

            var usedNodes = new HashSet<NodeContent>();
            int restored = 0;

            foreach (var kv in transforms)
            {
                string name = (kv.Key ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                NodeContent node = FindBestRigidRestoreTarget(input, allNodes, name, usedNodes);
                if (node == null)
                {
                    if (debugLog && context != null)
                    {
                        try
                        {
                            context.Logger.LogMessage(
                                "[CMZ] Could not find imported node for rigid transform restore '{0}' from '{1}'.",
                                name,
                                restoreFile);
                        }
                        catch { }
                    }

                    continue;
                }

                Matrix before = node.Transform;
                Matrix after;
                string mode;

                Matrix originalImporter = ConvertRigidSidecarTransformToFbxImporterSpace(kv.Value.OriginalTransform);

                if (kv.Value.HasAuthoringTransform)
                {
                    Matrix authoringImporter = ConvertRigidSidecarTransformToFbxImporterSpace(kv.Value.AuthoringTransform);

                    // The FBX imported nodes we receive here are not just the authored local
                    // node transforms. Blender has already baked the exported RootNode
                    // authoring Location/Rotation into root-level children. Therefore the
                    // sidecar delta must compare:
                    //
                    //   authored local transform + exported RootNode authoring transform
                    //        -> original game local transform
                    //
                    // Older code used only the local authored transform, which fixed the
                    // basis but rotated/transposed the node translation around the origin.
                    // That is what made the model appear under the hand or off to the side.
                    try
                    {
                        Matrix authoringWorldImporter = hasRootAuthoring
                            ? authoringImporter * rootAuthoringImporter
                            : authoringImporter;

                        Matrix.Invert(ref authoringWorldImporter, out Matrix inverseAuthoringWorld);

                        Matrix delta = inverseAuthoringWorld * originalImporter;
                        after = before * delta;
                        mode = hasRootAuthoring ? "delta-world" : "delta-local";
                    }
                    catch
                    {
                        after = originalImporter;
                        mode = "legacy-fallback";
                    }
                }
                else
                {
                    // Legacy sidecar files only had the original transform. Keep the older
                    // replacement behavior for those, but new sidecars use the safer delta path.
                    after = originalImporter;
                    mode = "legacy-replace";
                }

                node.Transform = after;
                usedNodes.Add(node);
                restored++;

                if (debugLog && context != null)
                {
                    try
                    {
                        context.Logger.LogMessage(
                            "[CMZ] Restored rigid node transform for '{0}' using sidecar entry '{1}' from '{2}' mode={3}. before={4} after={5}",
                            node.Name ?? "<unnamed>",
                            name,
                            restoreFile,
                            mode,
                            FormatMatrix(before),
                            FormatMatrix(after));
                    }
                    catch { }
                }
            }

            foreach (var kv in rotations)
            {
                string name = (kv.Key ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name) || transforms.ContainsKey(name))
                    continue;

                NodeContent node = FindBestRigidRestoreTarget(input, allNodes, name, usedNodes);
                if (node == null)
                    continue;

                if (!node.Transform.Decompose(out Vector3 scale, out Quaternion currentRotation, out Vector3 translation))
                    continue;

                node.Transform =
                    Matrix.CreateScale(scale) *
                    Matrix.CreateFromQuaternion(kv.Value) *
                    Matrix.CreateTranslation(translation);

                usedNodes.Add(node);
                restored++;

                if (debugLog && context != null)
                {
                    try
                    {
                        context.Logger.LogMessage(
                            "[CMZ] Restored rigid mesh rotation for '{0}' using sidecar entry '{1}' from '{2}'. RotationWXYZ=({3}, {4}, {5}, {6}).",
                            node.Name ?? "<unnamed>",
                            name,
                            restoreFile,
                            kv.Value.W.ToString(CultureInfo.InvariantCulture),
                            kv.Value.X.ToString(CultureInfo.InvariantCulture),
                            kv.Value.Y.ToString(CultureInfo.InvariantCulture),
                            kv.Value.Z.ToString(CultureInfo.InvariantCulture));
                    }
                    catch { }
                }
            }

            return restored;
        }

        /// <summary>
        /// Holds one sidecar restore entry, including the original game transform and the
        /// Blender-friendly authoring transform when available.
        /// </summary>
        private sealed class RigidNodeTransformRestore
        {
            /// <summary>Original local transform from the source model, scaled to FBX importer units.</summary>
            public Matrix OriginalTransform;

            /// <summary>Blender-friendly exported local transform, scaled to FBX importer units.</summary>
            public Matrix AuthoringTransform;

            /// <summary>Whether this entry has a matching authoring transform for delta restore.</summary>
            public bool HasAuthoringTransform;
        }

        /// <summary>
        /// Returns whether the supplied sidecar contains full rigid node transform restore data.
        /// </summary>
        public static bool HasRigidNodeTransformRestoreFile(string restoreFile)
        {
            return ReadRigidNodeTransformRestoreFile(restoreFile, DefaultRigidMeshRestoreUnitScale).Count > 0;
        }

        /// <summary>
        /// Clears RootNode authoring transforms before a full rigid sidecar restore is applied.
        /// </summary>
        /// <remarks>
        /// Full rigid sidecars already account for the authoring root transform through their
        /// restore delta. Resetting RootNode prevents the authoring offset from being applied twice.
        /// </remarks>
        public static void ResetAuthoringRootTransforms(
            NodeContent input,
            ContentProcessorContext context,
            bool debugLog)
        {
            if (input == null)
                return;

            var roots = new List<NodeContent>();
            if (!string.IsNullOrWhiteSpace(input.Name) &&
                input.Name.Trim().Equals("RootNode", StringComparison.OrdinalIgnoreCase))
            {
                roots.Add(input);
            }
            else
            {
                CollectMatchingNodes(input, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "RootNode" }, roots);
            }

            if (roots.Count == 0)
                roots.Add(input);

            var seen = new HashSet<NodeContent>();
            foreach (var root in roots)
            {
                if (root == null || !seen.Add(root))
                    continue;

                root.Transform = Matrix.Identity;

                if (debugLog && context != null)
                {
                    try
                    {
                        context.Logger.LogMessage(
                            "[CMZ] Reset authoring/root transform on '{0}' because a full rigid restore sidecar is being applied.",
                            root.Name ?? "<unnamed>");
                    }
                    catch { }
                }
            }
        }
        #endregion

        #region Diagnostics

        /// <summary>
        /// Writes matching imported NodeContent transforms before ModelProcessor runs.
        /// </summary>
        public static void LogMatchingInputNodes(
            NodeContent input,
            string nodeNames,
            ContentProcessorContext context,
            string label)
        {
            if (input == null || context == null)
                return;

            var names = ParseNameSet(nodeNames, DefaultSocketNodeNames);
            if (names.Count == 0)
                return;

            var matches = new List<NodeContent>();
            CollectMatchingNodes(input, names, matches);

            foreach (var node in matches)
            {
                if (node == null)
                    continue;

                try
                {
                    Matrix rootTransform = ComputeTransformRelativeToAncestor(node, input);
                    context.Logger.LogMessage(
                        "[CMZ] {0} input node '{1}' parent='{2}' local={3} root={4}",
                        label ?? "Socket",
                        node.Name ?? "<unnamed>",
                        node.Parent != null ? (node.Parent.Name ?? "<unnamed>") : "<none>",
                        FormatMatrix(node.Transform),
                        FormatMatrix(rootTransform));
                }
                catch { }
            }
        }

        /// <summary>
        /// Writes matching processed ModelContent bone transforms after ModelProcessor runs.
        /// </summary>
        public static void LogMatchingModelBones(
            ModelContent model,
            string boneNames,
            ContentProcessorContext context,
            string label)
        {
            if (model == null || model.Bones == null || context == null)
                return;

            var names = ParseNameSet(boneNames, DefaultSocketNodeNames);
            if (names.Count == 0)
                return;

            foreach (ModelBoneContent bone in model.Bones)
            {
                if (bone == null || string.IsNullOrWhiteSpace(bone.Name))
                    continue;

                if (!names.Contains(bone.Name.Trim()))
                    continue;

                try
                {
                    context.Logger.LogMessage(
                        "[CMZ] {0} output bone '{1}' parent='{2}' transform={3}",
                        label ?? "Socket",
                        bone.Name,
                        bone.Parent != null ? (bone.Parent.Name ?? "<unnamed>") : "<none>",
                        FormatMatrix(bone.Transform));
                }
                catch { }
            }
        }
        #endregion

        #region Socket Bake / Post Process

        /// <summary>
        /// Moves named socket/helper nodes directly under the imported model root while
        /// preserving their model-space transform.
        ///
        /// This is the part that fixes the "BarrelTip is left/back near the trigger" issue:
        /// Blender often exports the gun under a scaled parent such as "Gun Test". If BarrelTip
        /// remains a child of that parent, CMZ later reads the wrong local transform directly.
        /// Baking it to model-root space makes the transform self-contained before XNA writes it.
        /// </summary>
        public static void BakeNamedSocketNodesToModelRoot(
            NodeContent input,
            string socketBakeToModelRootNames,
            ContentProcessorContext context,
            bool debugLog)
        {
            if (input == null)
                return;

            var bakeNames = ParseNameSet(socketBakeToModelRootNames, DefaultSocketBakeToModelRootNames);
            if (bakeNames.Count == 0)
                return;

            var matches = new List<NodeContent>();
            CollectMatchingNodes(input, bakeNames, matches);

            foreach (var node in matches)
            {
                if (node == null || ReferenceEquals(node, input))
                    continue;

                // Include the imported root transform too. Some Blender/FBX files put the
                // FbxComp/root scale on the imported model root itself; CMZ later reads
                // BarrelTip.Transform directly, so the socket needs that full model-space value.
                Matrix modelRootTransform = ComputeTransformRelativeToAncestor(node, input);

                if (debugLog && context != null)
                {
                    try
                    {
                        context.Logger.LogMessage(
                            "[CMZ] Baking socket '{0}' to model root. Old parent='{1}', root-space T=({2}, {3}, {4}).",
                            node.Name ?? "<unnamed>",
                            node.Parent != null ? (node.Parent.Name ?? "<unnamed>") : "<none>",
                            modelRootTransform.M41.ToString(CultureInfo.InvariantCulture),
                            modelRootTransform.M42.ToString(CultureInfo.InvariantCulture),
                            modelRootTransform.M43.ToString(CultureInfo.InvariantCulture));
                    }
                    catch { }
                }

                node.Transform = modelRootTransform;

                // If it is already a direct child of the root, the transform update above is enough.
                if (ReferenceEquals(node.Parent, input))
                    continue;

                try
                {
                    NodeContent oldParent = node.Parent;
                    oldParent?.Children.Remove(node);

                    input.Children.Add(node);
                }
                catch
                {
                    // If a particular XNA collection refuses reparenting, keep the corrected
                    // transform. The later ModelContent pass still has a chance to fix the bone.
                }
            }
        }

        /// <summary>
        /// Applies scale/orientation compensation to named output bones after ModelProcessor.
        /// </summary>
        public static void FixNamedSocketBones(
            ModelContent model,
            float socketBasisScale,
            float socketTranslationScale,
            string socketNodeNames,
            bool enableSocketRotationCorrection,
            string socketRotationCorrectionNames,
            string socketRotationCorrectionAxis,
            float socketRotationCorrectionDegrees,
            string socketBasisTransform = null)
        {
            if (model == null || model.Bones == null || model.Bones.Count == 0)
                return;

            if (!IsUsablePositiveScale(socketBasisScale))
                socketBasisScale = DefaultSocketBasisScale;

            float effectiveTranslationScale = IsUsablePositiveScale(socketTranslationScale)
                ? socketTranslationScale
                : socketBasisScale;

            bool shouldScaleBasis = IsUsableNonIdentityScale(socketBasisScale);
            bool shouldScaleTranslation = IsUsableNonIdentityScale(effectiveTranslationScale);

            var scaleNames = (shouldScaleBasis || shouldScaleTranslation)
                ? ParseNameSet(socketNodeNames, DefaultSocketNodeNames)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            bool shouldRotate = enableSocketRotationCorrection &&
                                Math.Abs(socketRotationCorrectionDegrees) > 0.0001f;
            var rotateNames = shouldRotate
                ? ParseNameSet(socketRotationCorrectionNames, DefaultSocketRotationCorrectionNames)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (scaleNames.Count == 0 && rotateNames.Count == 0)
                return;

            Matrix rotationCorrection = shouldRotate
                ? CreateAxisRotation(socketRotationCorrectionAxis, socketRotationCorrectionDegrees)
                : Matrix.Identity;

            foreach (ModelBoneContent bone in model.Bones)
            {
                if (bone == null || string.IsNullOrWhiteSpace(bone.Name))
                    continue;

                string name = bone.Name.Trim();
                bool applyScale = scaleNames.Contains(name);
                bool applyRotation = rotateNames.Contains(name);

                if (!applyScale && !applyRotation)
                    continue;

                Matrix transform = bone.Transform;

                if (applyRotation)
                {
                    // Prepend for local-basis correction without rotating the translation row.
                    transform = rotationCorrection * transform;
                }

                if (applyScale)
                {
                    // Keep basis scale and translation scale independent, but by default both
                    // use the same Scale value. Do not force translation to 0.01 here; that
                    // ignores the FbxComp multiplier and is why the previous fix barely moved it.
                    if (shouldScaleBasis)
                    {
                        if (IsBlenderGlbRoundTripForwardBasisTransform(socketBasisTransform))
                            RemapBlenderGlbRoundTripBasis(ref transform, socketBasisScale, flipForward: true);
                        else if (IsBlenderGlbRoundTripBasisTransform(socketBasisTransform))
                            RemapBlenderGlbRoundTripBasis(ref transform, socketBasisScale, flipForward: false);
                        else
                            ScaleBasisOnly(ref transform, socketBasisScale);
                    }

                    if (shouldScaleTranslation)
                        ScaleTranslationOnly(ref transform, effectiveTranslationScale);
                }

                bone.Transform = transform;
            }
        }

        /// <summary>
        /// Back-compatible entry point used by older patches.
        /// </summary>
        public static void ScaleNamedSocketBones(ModelContent model, float scale, string socketNodeNames)
        {
            FixNamedSocketBones(
                model,
                socketBasisScale: scale,
                socketTranslationScale: scale,
                socketNodeNames: socketNodeNames,
                enableSocketRotationCorrection: false,
                socketRotationCorrectionNames: DefaultSocketRotationCorrectionNames,
                socketRotationCorrectionAxis: DefaultSocketRotationCorrectionAxis,
                socketRotationCorrectionDegrees: DefaultSocketRotationCorrectionDegrees);
        }
        #endregion

        #region Node Search Helpers

        /// <summary>
        /// Recursively collects every imported content node beneath the supplied node.
        /// </summary>
        private static void CollectAllNodes(NodeContent node, List<NodeContent> nodes)
        {
            if (node == null || nodes == null)
                return;

            nodes.Add(node);

            foreach (NodeContent child in node.Children)
                CollectAllNodes(child, nodes);
        }

        /// <summary>
        /// Finds the imported node that best matches a sidecar restore entry.
        /// </summary>
        /// <remarks>
        /// Matching tries exact names first, then normalized names, then a controlled fallback for
        /// primary mesh nodes because Blender/FBX can rename or collapse GLB mesh parents.
        /// </remarks>
        private static NodeContent FindBestRigidRestoreTarget(
            NodeContent input,
            List<NodeContent> allNodes,
            string restoreName,
            HashSet<NodeContent> usedNodes)
        {
            if (allNodes == null || string.IsNullOrWhiteSpace(restoreName))
                return null;

            string wanted = restoreName.Trim();
            string wantedKey = NormalizeNodeKey(wanted);

            foreach (var node in allNodes)
            {
                if (node == null || (usedNodes != null && usedNodes.Contains(node)))
                    continue;

                if (!string.IsNullOrWhiteSpace(node.Name) &&
                    node.Name.Trim().Equals(wanted, StringComparison.OrdinalIgnoreCase))
                    return node;
            }

            if (wantedKey.Length > 0)
            {
                foreach (var node in allNodes)
                {
                    if (node == null || (usedNodes != null && usedNodes.Contains(node)))
                        continue;

                    string key = NormalizeNodeKey(node.Name);
                    if (key.Length > 0 && key.Equals(wantedKey, StringComparison.OrdinalIgnoreCase))
                        return node;
                }

                foreach (var node in allNodes)
                {
                    if (node == null || (usedNodes != null && usedNodes.Contains(node)))
                        continue;

                    string key = NormalizeNodeKey(node.Name);
                    if (key.Length > 0 &&
                        (key.Contains(wantedKey) || wantedKey.Contains(key)))
                        return node;
                }
            }

            // Blender/FBX can rename/collapse the primary mesh node, especially when a GLB is
            // edited and exported as FBX. If the sidecar primary entry was not an exact name
            // match, apply it to the best root-level mesh branch instead of silently leaving the
            // visible weapon at the clean Blender authoring rotation.
            if (IsPrimaryRigidRestoreName(wanted))
            {
                NodeContent best = null;
                foreach (var node in allNodes)
                {
                    if (node == null || ReferenceEquals(node, input) ||
                        (usedNodes != null && usedNodes.Contains(node)))
                        continue;

                    if (!HasMeshDescendant(node))
                        continue;

                    if (IsHelperLikeNodeName(node.Name))
                        continue;

                    if (best == null)
                        best = node;

                    string key = NormalizeNodeKey(node.Name);
                    if (key.Contains("gun") || key.Contains("weap") || key.Contains("weapon") || key.Contains("mesh"))
                        return node;
                }

                return best;
            }

            return null;
        }

        /// <summary>
        /// Returns whether a sidecar entry name looks like a primary visible mesh restore entry.
        /// </summary>
        private static bool IsPrimaryRigidRestoreName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            string key = NormalizeNodeKey(name);
            if (key.Length == 0)
                return false;

            if (key.Contains("barrel") || key.Contains("flame") || key.Contains("tip") || key.Contains("gem"))
                return false;

            return true;
        }

        /// <summary>
        /// Returns whether a node name appears to describe a helper/socket rather than a visible mesh.
        /// </summary>
        private static bool IsHelperLikeNodeName(string name)
        {
            string key = NormalizeNodeKey(name);
            if (key.Length == 0)
                return false;

            return key.Contains("barrel") || key.Contains("flame") || key.Contains("tip") || key.Contains("gem");
        }

        /// <summary>
        /// Returns whether this node or any child node contains mesh content.
        /// </summary>
        private static bool HasMeshDescendant(NodeContent node)
        {
            if (node == null)
                return false;

            if (node is MeshContent)
                return true;

            foreach (NodeContent child in node.Children)
            {
                if (HasMeshDescendant(child))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Produces a loose comparison key by removing punctuation/spacing and lowercasing letters.
        /// </summary>
        private static string NormalizeNodeKey(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            var chars = new List<char>();
            foreach (char ch in name.Trim())
            {
                if (char.IsLetterOrDigit(ch))
                    chars.Add(char.ToLowerInvariant(ch));
            }

            return new string(chars.ToArray());
        }

        /// <summary>
        /// Recursively collects imported nodes whose trimmed names match one of the requested names.
        /// </summary>
        private static void CollectMatchingNodes(NodeContent node, HashSet<string> names, List<NodeContent> matches)
        {
            if (node == null)
                return;

            if (!string.IsNullOrWhiteSpace(node.Name) && names.Contains(node.Name.Trim()))
                matches.Add(node);

            foreach (NodeContent child in node.Children)
                CollectMatchingNodes(child, names, matches);
        }
        #endregion

        #region Matrix / Basis Helpers

        /// <summary>
        /// Computes a node transform relative to an ancestor by walking up the imported hierarchy.
        /// </summary>
        private static Matrix ComputeTransformRelativeToAncestor(NodeContent node, NodeContent ancestor)
        {
            Matrix result = Matrix.Identity;

            NodeContent cursor = node;
            bool gotNode = false;

            while (cursor != null)
            {
                result = gotNode ? (result * cursor.Transform) : cursor.Transform;
                gotNode = true;

                if (ReferenceEquals(cursor, ancestor))
                    break;

                cursor = cursor.Parent;
            }

            return result;
        }

        /// <summary>
        /// Scales only the basis rows of a transform matrix, leaving translation untouched.
        /// </summary>
        private static void ScaleBasisOnly(ref Matrix m, float scale)
        {
            m.M11 *= scale; m.M12 *= scale; m.M13 *= scale;
            m.M21 *= scale; m.M22 *= scale; m.M23 *= scale;
            m.M31 *= scale; m.M32 *= scale; m.M33 *= scale;
        }

        /// <summary>
        /// Scales only the translation row of a transform matrix.
        /// </summary>
        private static void ScaleTranslationOnly(ref Matrix m, float scale)
        {
            m.M41 *= scale;
            m.M42 *= scale;
            m.M43 *= scale;
        }

        /// <summary>
        /// Remaps a Blender GLB-to-FBX round-tripped socket basis back into the expected CMZ basis.
        /// </summary>
        private static void RemapBlenderGlbRoundTripBasis(ref Matrix m, float scale, bool flipForward)
        {
            float m11 = m.M11, m12 = m.M12, m13 = m.M13;
            float m21 = m.M21, m22 = m.M22, m23 = m.M23;
            float m31 = m.M31, m32 = m.M32, m33 = m.M33;

            // Blender GLB -> FBX -> XNA imports this socket basis transposed with one axis
            // sign-flipped. Rebuild the basis to match the original GLB socket orientation:
            //
            //   [ m11, m31, -m21 ]
            //   [ m12, m32, -m22 ]
            //   [ m13, m33, -m23 ]
            //
            // Translation is intentionally untouched here.
            float rightSign = flipForward ? -1f : 1f;
            float forwardSign = flipForward ? -1f : 1f;

            m.M11 = rightSign * m11 * scale;
            m.M12 = rightSign * m31 * scale;
            m.M13 = rightSign * -m21 * scale;

            m.M21 = m12 * scale;
            m.M22 = m32 * scale;
            m.M23 = -m22 * scale;

            m.M31 = forwardSign * m13 * scale;
            m.M32 = forwardSign * m33 * scale;
            m.M33 = forwardSign * -m23 * scale;
        }

        /// <summary>
        /// Returns whether the socket basis transform mode requests the standard GLB round-trip remap.
        /// </summary>
        private static bool IsBlenderGlbRoundTripBasisTransform(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode))
                return false;

            mode = mode.Trim();
            return mode.Equals("BlenderGlbRoundTrip", StringComparison.OrdinalIgnoreCase) ||
                   mode.Equals("GlbRoundTrip", StringComparison.OrdinalIgnoreCase) ||
                   mode.Equals("RoundTrip", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns whether the socket basis transform mode requests the forward-flipped round-trip remap.
        /// </summary>
        private static bool IsBlenderGlbRoundTripForwardBasisTransform(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode))
                return false;

            mode = mode.Trim();
            return mode.Equals("BlenderGlbRoundTripForward", StringComparison.OrdinalIgnoreCase) ||
                   mode.Equals("GlbRoundTripForward", StringComparison.OrdinalIgnoreCase) ||
                   mode.Equals("RoundTripForward", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns whether a scale value is positive, finite, and usable.
        /// </summary>
        private static bool IsUsablePositiveScale(float scale)
        {
            if (scale <= 0f || float.IsNaN(scale) || float.IsInfinity(scale))
                return false;

            return true;
        }

        /// <summary>
        /// Returns whether a scale value is usable and meaningfully different from 1.
        /// </summary>
        private static bool IsUsableNonIdentityScale(float scale)
        {
            if (!IsUsablePositiveScale(scale))
                return false;

            return Math.Abs(scale - 1f) > 0.000001f;
        }

        /// <summary>
        /// Creates a simple X/Y/Z axis rotation matrix from config-friendly axis text.
        /// </summary>
        private static Matrix CreateAxisRotation(string axis, float degrees)
        {
            float radians = MathHelper.ToRadians(degrees);
            string a = (axis ?? "").Trim();

            if (a.Equals("X", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("Right", StringComparison.OrdinalIgnoreCase))
                return Matrix.CreateRotationX(radians);

            if (a.Equals("Z", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("Forward", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("Backward", StringComparison.OrdinalIgnoreCase))
                return Matrix.CreateRotationZ(radians);

            return Matrix.CreateRotationY(radians);
        }

        /// <summary>
        /// Builds the importer-space authoring transform from Blender-facing location/rotation strings.
        /// </summary>
        private static Matrix CreateAuthoringTransformMatrix(
            string authoringLocation,
            string authoringRotation,
            string authoringRotationQuaternion,
            float authoringLocationScale)
        {
            Vector3 location = BlenderLocationToGltf(ParseVector3(authoringLocation, Vector3.Zero));
            Quaternion rotation = BlenderQuaternionToGltf(ParseAuthoringRotation(authoringRotation, authoringRotationQuaternion, Quaternion.Identity));

            if (authoringLocationScale <= 0f ||
                float.IsNaN(authoringLocationScale) ||
                float.IsInfinity(authoringLocationScale))
            {
                authoringLocationScale = DefaultAuthoringLocationScale;
            }

            location *= authoringLocationScale;
            return Matrix.CreateFromQuaternion(rotation) * Matrix.CreateTranslation(location);
        }

        /// <summary>
        /// Returns whether a matrix is close enough to identity to be treated as a no-op.
        /// </summary>
        private static bool IsNearlyIdentity(Matrix m)
        {
            const float epsilon = 0.00001f;

            return Math.Abs(m.M11 - 1f) <= epsilon && Math.Abs(m.M12) <= epsilon && Math.Abs(m.M13) <= epsilon && Math.Abs(m.M14) <= epsilon &&
                   Math.Abs(m.M21) <= epsilon && Math.Abs(m.M22 - 1f) <= epsilon && Math.Abs(m.M23) <= epsilon && Math.Abs(m.M24) <= epsilon &&
                   Math.Abs(m.M31) <= epsilon && Math.Abs(m.M32) <= epsilon && Math.Abs(m.M33 - 1f) <= epsilon && Math.Abs(m.M34) <= epsilon &&
                   Math.Abs(m.M41) <= epsilon && Math.Abs(m.M42) <= epsilon && Math.Abs(m.M43) <= epsilon && Math.Abs(m.M44 - 1f) <= epsilon;
        }
        #endregion

        #region Parsing Helpers

        /// <summary>
        /// Parses a three-component vector from comma/semicolon/pipe separated text.
        /// </summary>
        private static Vector3 ParseVector3(string text, Vector3 fallback)
        {
            var values = ParseFloatList(text, 3);
            if (values == null)
                return fallback;

            return new Vector3(values[0], values[1], values[2]);
        }

        /// <summary>
        /// Converts Blender RootNode UI location values into glTF/FBX importer basis.
        /// </summary>
        private static Vector3 BlenderLocationToGltf(Vector3 blender)
        {
            // User-facing authoring values are copied from Blender's RootNode transform UI.
            // Convert Blender Z-up object coordinates into raw glTF/FBX importer coordinates:
            //   Blender (X, Y, Z) -> glTF/FBX (X, Z, -Y)
            return new Vector3(blender.X, blender.Z, -blender.Y);
        }

        /// <summary>
        /// Parses a quaternion from W,X,Y,Z text into XNA's X,Y,Z,W quaternion storage.
        /// </summary>
        private static Quaternion ParseQuaternionWxyz(string text, Quaternion fallback)
        {
            var values = ParseFloatList(text, 4);
            if (values == null)
                return fallback;

            var q = new Quaternion(values[1], values[2], values[3], values[0]);
            float len = q.LengthSquared();
            if (len < 0.0000001f || float.IsNaN(len) || float.IsInfinity(len))
                return fallback;

            q.Normalize();
            return q;
        }

        /// <summary>
        /// Parses authoring rotation from flexible Euler/quaternion text with legacy quaternion fallback.
        /// </summary>
        /// <remarks>
        /// Three values are interpreted as Euler degrees X,Y,Z. Four values are interpreted as
        /// Blender-style quaternion W,X,Y,Z. If the flexible value is missing or invalid, the
        /// legacy quaternion text is used.
        /// </remarks>
        private static Quaternion ParseAuthoringRotation(string flexibleText, string quaternionText, Quaternion fallback)
        {
            if (!string.IsNullOrWhiteSpace(flexibleText))
            {
                var values = ParseFloatListAny(flexibleText);
                if (values != null)
                {
                    if (values.Length == 3)
                        return QuaternionFromEulerDegrees(values[0], values[1], values[2], fallback);

                    if (values.Length == 4)
                        return NormalizeQuaternionWxyz(values[0], values[1], values[2], values[3], fallback);
                }
            }

            return ParseQuaternionWxyz(quaternionText, fallback);
        }


        /// <summary>
        /// Converts a sidecar transform from GLB-side basis into the FBX importer basis.
        /// </summary>
        private static Matrix ConvertRigidSidecarTransformToFbxImporterSpace(Matrix transform)
        {
            // TexturePacks writes sidecar transforms in the same GLB-side basis used for the
            // exported authoring file. XNA's FBX importer presents the local basis as:
            //   row0 =  glb row0
            //   row1 = -glb row2
            //   row2 =  glb row1
            // with local translation kept in the same component order. This is the mapping
            // observed from the original pistol BarrelTip before any socket post-process.
            Matrix converted = transform;

            converted.M11 = transform.M11;
            converted.M12 = transform.M12;
            converted.M13 = transform.M13;
            converted.M14 = transform.M14;

            converted.M21 = -transform.M31;
            converted.M22 = -transform.M32;
            converted.M23 = -transform.M33;
            converted.M24 = -transform.M34;

            converted.M31 = transform.M21;
            converted.M32 = transform.M22;
            converted.M33 = transform.M23;
            converted.M34 = transform.M24;

            converted.M41 = transform.M41;
            converted.M42 = transform.M42;
            converted.M43 = transform.M43;
            converted.M44 = transform.M44;

            return converted;
        }

        /// <summary>
        /// Returns whether a sidecar restore name looks like a socket/helper transform.
        /// </summary>
        /// <remarks>
        /// Kept as a small classification helper for future restore-mode branching.
        /// </remarks>
        private static bool IsSocketTransformRestoreName(string name)
        {
            string key = NormalizeNodeKey(name);
            if (key.Length == 0)
                return false;

            return key.Contains("barrel") || key.Contains("flame") || key.Contains("tip");
        }

        /// <summary>
        /// Reads legacy rotation-only rigid mesh restore metadata from a sidecar file.
        /// </summary>
        private static Dictionary<string, Quaternion> ReadRigidMeshRotationRestoreFile(string restoreFile)
        {
            var rotations = new Dictionary<string, Quaternion>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (string.IsNullOrWhiteSpace(restoreFile) || !File.Exists(restoreFile))
                    return rotations;

                bool inSection = false;
                foreach (var raw in File.ReadAllLines(restoreFile))
                {
                    string line = (raw ?? "").Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#"))
                        continue;

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        string section = line.Substring(1, line.Length - 2).Trim();
                        inSection = section.Equals("RigidMeshRotations", StringComparison.OrdinalIgnoreCase);
                        continue;
                    }

                    if (!inSection)
                        continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                        continue;

                    string name = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();
                    var q = ParseQuaternionWxyz(value, Quaternion.Identity);
                    if (!string.IsNullOrWhiteSpace(name))
                        rotations[name] = q;
                }
            }
            catch { }

            return rotations;
        }

        /// <summary>
        /// Reads full rigid node transform restore metadata from a sidecar file.
        /// </summary>
        private static Dictionary<string, RigidNodeTransformRestore> ReadRigidNodeTransformRestoreFile(string restoreFile, float unitScale)
        {
            var originals = new Dictionary<string, Matrix>(StringComparer.OrdinalIgnoreCase);
            var authoring = new Dictionary<string, Matrix>(StringComparer.OrdinalIgnoreCase);
            var result = new Dictionary<string, RigidNodeTransformRestore>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (string.IsNullOrWhiteSpace(restoreFile) || !File.Exists(restoreFile))
                    return result;

                string section = string.Empty;
                foreach (var raw in File.ReadAllLines(restoreFile))
                {
                    string line = (raw ?? "").Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#"))
                        continue;

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        section = line.Substring(1, line.Length - 2).Trim();
                        continue;
                    }

                    bool isOriginal = section.Equals("RigidNodeTransforms", StringComparison.OrdinalIgnoreCase);
                    bool isAuthoring = section.Equals("RigidNodeAuthoringTransforms", StringComparison.OrdinalIgnoreCase) ||
                                       section.Equals("RigidNodeExportTransforms", StringComparison.OrdinalIgnoreCase);
                    if (!isOriginal && !isAuthoring)
                        continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                        continue;

                    string name = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();
                    if (!TryParseRigidTransformValue(value, unitScale, out Matrix transform) || string.IsNullOrWhiteSpace(name))
                        continue;

                    if (isOriginal)
                        originals[name] = transform;
                    else
                        authoring[name] = transform;
                }

                foreach (var kv in originals)
                {
                    var entry = new RigidNodeTransformRestore
                    {
                        OriginalTransform = kv.Value,
                        AuthoringTransform = Matrix.Identity,
                        HasAuthoringTransform = false
                    };

                    if (authoring.TryGetValue(kv.Key, out Matrix authoringTransform))
                    {
                        entry.AuthoringTransform = authoringTransform;
                        entry.HasAuthoringTransform = true;
                    }

                    result[kv.Key] = entry;
                }
            }
            catch { }

            return result;
        }

        /// <summary>
        /// Parses one sidecar transform value into an XNA matrix, applying the requested unit scale.
        /// </summary>
        private static bool TryParseRigidTransformValue(string value, float unitScale, out Matrix transform)
        {
            transform = Matrix.Identity;
            var values = ParseFloatList(value, 10);
            if (values == null)
                return false;

            if (unitScale <= 0f || float.IsNaN(unitScale) || float.IsInfinity(unitScale))
                unitScale = 1f;

            var scale = new Vector3(values[0], values[1], values[2]) * unitScale;
            var rotation = NormalizeQuaternionWxyz(values[3], values[4], values[5], values[6], Quaternion.Identity);
            var translation = new Vector3(values[7], values[8], values[9]) * unitScale;

            transform =
                Matrix.CreateScale(scale) *
                Matrix.CreateFromQuaternion(rotation) *
                Matrix.CreateTranslation(translation);
            return true;
        }

        /// <summary>
        /// Builds a normalized quaternion from Blender-style Euler degrees X,Y,Z.
        /// </summary>
        private static Quaternion QuaternionFromEulerDegrees(float xDegrees, float yDegrees, float zDegrees, Quaternion fallback)
        {
            const float degToRad = (float)(Math.PI / 180.0);
            var qx = Quaternion.CreateFromAxisAngle(Vector3.Right, xDegrees * degToRad);
            var qy = Quaternion.CreateFromAxisAngle(Vector3.Up, yDegrees * degToRad);
            var qz = Quaternion.CreateFromAxisAngle(Vector3.Backward, zDegrees * degToRad);
            var q = qx * qy * qz;

            float len = q.LengthSquared();
            if (len < 0.0000001f || float.IsNaN(len) || float.IsInfinity(len))
                return fallback;

            q.Normalize();
            return q;
        }

        /// <summary>
        /// Builds and normalizes a quaternion from W,X,Y,Z values, returning fallback if invalid.
        /// </summary>
        private static Quaternion NormalizeQuaternionWxyz(float w, float x, float y, float z, Quaternion fallback)
        {
            var q = new Quaternion(x, y, z, w);
            float len = q.LengthSquared();
            if (len < 0.0000001f || float.IsNaN(len) || float.IsInfinity(len))
                return fallback;

            q.Normalize();
            return q;
        }

        /// <summary>
        /// Converts a Blender-facing quaternion into glTF/FBX importer basis.
        /// </summary>
        private static Quaternion BlenderQuaternionToGltf(Quaternion blender)
        {
            // User-facing authoring quaternion is Blender W,X,Y,Z.
            // Convert basis to raw glTF/FBX importer coordinates:
            //   glTF/FBX W,X,Y,Z = Blender W,X,Z,-Y
            var gltf = new Quaternion(blender.X, blender.Z, -blender.Y, blender.W);
            float len = gltf.LengthSquared();
            if (len < 0.0000001f || float.IsNaN(len) || float.IsInfinity(len))
                return Quaternion.Identity;

            gltf.Normalize();
            return gltf;
        }

        /// <summary>
        /// Parses exactly the requested number of float values from separated text.
        /// </summary>
        private static float[] ParseFloatList(string text, int count)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var parts = text.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < count)
                return null;

            var values = new float[count];
            for (int i = 0; i < count; i++)
            {
                string s = (parts[i] ?? "").Trim().Replace(',', '.');
                if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                    return null;
            }

            return values;
        }

        /// <summary>
        /// Parses either three or four float values from separated text.
        /// </summary>
        private static float[] ParseFloatListAny(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var parts = text.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 && parts.Length != 4)
                return null;

            var values = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                string s = (parts[i] ?? "").Trim().Replace(',', '.');
                if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                    return null;
            }

            return values;
        }
        #endregion

        #region Formatting / Name Parsing

        /// <summary>
        /// Formats a matrix compactly for debug log output.
        /// </summary>
        private static string FormatMatrix(Matrix m)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "[[{0:0.######}, {1:0.######}, {2:0.######}, {3:0.######}], [{4:0.######}, {5:0.######}, {6:0.######}, {7:0.######}], [{8:0.######}, {9:0.######}, {10:0.######}, {11:0.######}], [{12:0.######}, {13:0.######}, {14:0.######}, {15:0.######}]]",
                m.M11, m.M12, m.M13, m.M14,
                m.M21, m.M22, m.M23, m.M24,
                m.M31, m.M32, m.M33, m.M34,
                m.M41, m.M42, m.M43, m.M44);
        }

        /// <summary>
        /// Parses a delimited node-name list, falling back to the supplied default list when empty.
        /// </summary>
        private static HashSet<string> ParseNameSet(string text, string defaultText)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(text))
                text = defaultText;

            foreach (var raw in text.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = (raw ?? "").Trim();
                if (name.Length > 0)
                    set.Add(name);
            }

            return set;
        }
        #endregion
    }
}