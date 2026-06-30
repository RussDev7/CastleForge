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
using DNA.Drawing.Animation;
using DNA.Drawing;
using System;

namespace CMZ.ContentPipeline
{
    /// <summary>
    /// =========================================================================================
    /// SkinedModelProcessor
    /// =========================================================================================
    ///
    /// Purpose
    /// -------
    /// XNA Content Pipeline processor for CMZ/DNA skinned models.
    ///
    /// Pipeline Responsibilities
    /// -------------------------
    /// 1) Run the stock XNA ModelProcessor:
    ///    - Imports meshes/materials
    ///    - Preserves/creates skinning vertex elements & channels
    ///
    /// 2) Attach CMZ/DNA runtime skinning metadata into ModelContent.Tag:
    ///    - Skeleton (DNA.Drawing.Skeleton)
    ///    - InverseBindPose (List<Matrix>)
    ///    - Animation clips dictionary (Dictionary<string, AnimationClip>) [currently empty]
    ///
    /// Runtime Expectations
    /// --------------------
    /// The runtime expects Model.Tag to contain SkinedAnimationData so it can:
    /// - Reconstruct/validate skeleton hierarchy
    /// - Apply inverse bind pose matrices for skin deformation
    /// - (Optionally) enumerate animation clips by name
    ///
    /// Notes / Limitations
    /// -------------------
    /// - This processor does NOT extract animation clips yet; it attaches an empty clip dictionary.
    ///   If a model requires animations, clip extraction must be added later.
    /// - Skeleton is built purely from the processed ModelContent.Bones list:
    ///   names + parent indices + local (bind pose) transforms.
    /// - InverseBindPose is computed from absolute bind pose matrices derived from that same hierarchy.
    ///
    /// =========================================================================================
    /// </summary>
    [ContentProcessor(DisplayName = "SkinedModelProcessor")]
    public sealed class SkinedModelProcessor : ModelProcessor
    {
        /// <summary>
        /// Comma/semicolon/pipe-separated socket/helper bone names that should receive the
        /// same Scale compensation as the visible mesh before DNA skeleton metadata is built.
        /// </summary>
        public string SocketNodeNames { get; set; } = ProcessorScaleUtility.DefaultSocketNodeNames;

        /// <summary>
        /// Translation scale for socket/helper bones before DNA skeleton metadata is built.
        /// A value <= 0 means use the inherited ModelProcessor Scale value.
        ///
        /// For TexturePacks round-trips, leave this automatic unless you are fixing a one-off
        /// asset. A value <= 0 uses the same Scale value as the mesh.
        /// </summary>
        public float SocketTranslationScale { get; set; } = ProcessorScaleUtility.AutoSocketTranslationScale;

        /// <summary>
        /// Optional BarrelTip basis correction before DNA skeleton metadata is built. Disabled
        /// by default because the normal fix is separate basis/translation scaling, not a
        /// hardcoded rotation flip.
        /// </summary>
        public bool SocketRotationCorrection { get; set; } = false;

        /// <summary>
        /// Socket names that should receive the basis correction. Defaults to BarrelTip only.
        /// </summary>
        public string SocketRotationCorrectionNames { get; set; } = ProcessorScaleUtility.DefaultSocketRotationCorrectionNames;

        /// <summary>
        /// Axis used for the socket-only basis correction. Valid values: X, Y, Z.
        /// </summary>
        public string SocketRotationCorrectionAxis { get; set; } = ProcessorScaleUtility.DefaultSocketRotationCorrectionAxis;

        /// <summary>
        /// Degrees used for the optional socket-only basis correction.
        /// </summary>
        public float SocketRotationCorrectionDegrees { get; set; } = ProcessorScaleUtility.DefaultSocketRotationCorrectionDegrees;

        /// <summary>
        /// Optional GLB authoring location to remove before processing. Format: X,Y,Z.
        /// Values should match TexturePacks [Models] AuthoringLocation.
        /// </summary>
        public string AuthoringLocation { get; set; } = ProcessorScaleUtility.DefaultAuthoringLocation;

        /// <summary>
        /// Optional GLB authoring rotation to remove before processing.
        /// Format is either Blender Euler degrees X,Y,Z or Blender Quaternion W,X,Y,Z.
        /// </summary>
        public string AuthoringRotation { get; set; } = ProcessorScaleUtility.DefaultAuthoringRotation;

        /// <summary>
        /// Optional GLB authoring rotation to remove before processing.
        /// Format is W,X,Y,Z, matching Blender's displayed quaternion order.
        /// Kept for compatibility; AuthoringRotation takes priority when provided.
        /// </summary>
        public string AuthoringRotationQuaternion { get; set; } = ProcessorScaleUtility.DefaultAuthoringRotationQuaternion;

        /// <summary>
        /// Converts authoring location from GLB/Blender units into FBX importer units.
        /// </summary>
        public float AuthoringLocationScale { get; set; } = ProcessorScaleUtility.DefaultAuthoringLocationScale;

        /// <summary>
        /// Optional TexturePacks .cmzrigid.ini sidecar used to restore original rigid node
        /// transforms after NormalizeRigidMeshRotation authoring cleanup.
        /// </summary>
        public string RigidMeshRestoreFile { get; set; } = ProcessorScaleUtility.DefaultRigidMeshRestoreFile;

        #region Content Pipeline Entry Point

        /// <summary>
        /// Main processor entry point invoked by the XNA content pipeline.
        /// </summary>
        public override ModelContent Process(NodeContent input, ContentProcessorContext context)
        {
            bool hasFullRigidRestore = ProcessorScaleUtility.HasRigidNodeTransformRestoreFile(RigidMeshRestoreFile);

            if (hasFullRigidRestore)
            {
                ProcessorScaleUtility.ResetAuthoringRootTransforms(input, context, debugLog: false);

                ProcessorScaleUtility.ApplyRigidMeshRotationRestore(
                    input,
                    RigidMeshRestoreFile,
                    AuthoringLocationScale,
                    AuthoringLocation,
                    AuthoringRotation,
                    AuthoringRotationQuaternion,
                    AuthoringLocationScale,
                    context,
                    debugLog: false);
            }
            else
            {
                ProcessorScaleUtility.ApplyInverseAuthoringTransform(
                    input,
                    AuthoringLocation,
                    AuthoringRotation,
                    AuthoringRotationQuaternion,
                    AuthoringLocationScale,
                    context,
                    debugLog: false);

                ProcessorScaleUtility.ApplyRigidMeshRotationRestore(
                    input,
                    RigidMeshRestoreFile,
                    AuthoringLocationScale,
                    AuthoringLocation,
                    AuthoringRotation,
                    AuthoringRotationQuaternion,
                    AuthoringLocationScale,
                    context,
                    debugLog: false);
            }

            // Let stock XNA do the heavy lifting (including skin vertex channels).
            var model = base.Process(input, context);

            // Stock ModelProcessor.Scale handles mesh geometry, but transform-only sockets
            // such as BarrelTip need the same position/scale/orientation round-trip compensation
            // before the DNA skeleton tag is built.
            ProcessorScaleUtility.FixNamedSocketBones(
                model,
                socketBasisScale: Scale,
                socketTranslationScale: SocketTranslationScale,
                socketNodeNames: SocketNodeNames,
                enableSocketRotationCorrection: SocketRotationCorrection,
                socketRotationCorrectionNames: SocketRotationCorrectionNames,
                socketRotationCorrectionAxis: SocketRotationCorrectionAxis,
                socketRotationCorrectionDegrees: SocketRotationCorrectionDegrees);

            // Build skeleton + inverse bind pose from the processed bones.
            AttachSkinningTag(model);

            return model;
        }
        #endregion

        #region Skinning Tag Attachment (SkinedAnimationData)

        /// <summary>
        /// Builds minimum viable skinning metadata from ModelContent.Bones and attaches it to Model.Tag.
        /// </summary>
        private static void AttachSkinningTag(ModelContent model)
        {
            if (model?.Bones == null || model.Bones.Count == 0)
                return;

            int n = model.Bones.Count;

            // Local (bind pose) transforms + names + parent indices.
            var local = new Matrix[n];
            var parents = new int[n];
            var names = new string[n];

            #region Collect Bone Locals / Names / Parents

            for (int i = 0; i < n; i++)
            {
                var b = model.Bones[i];

                local[i] = b.Transform;

                // Keep names stable (runtime bone lookups like "BarrelTip").
                names[i] = string.IsNullOrWhiteSpace(b.Name) ? $"Bone_{i}" : b.Name;

                parents[i] = (b.Parent != null) ? b.Parent.Index : -1;
            }
            #endregion

            #region Compute Absolute Bind Pose Matrices

            // Absolute bind pose (matches your runtime Skeleton.CopyAbsoluteBoneTransformsTo math):
            // abs = local * parentAbs
            var abs = new Matrix[n];
            for (int i = 0; i < n; i++)
            {
                int p = parents[i];
                abs[i] = (p < 0) ? local[i] : (local[i] * abs[p]);
            }
            #endregion

            #region Compute Inverse Bind Pose

            // Inverse bind pose array (same indexing as bones)
            var invBindPose = new List<Matrix>(n);
            for (int i = 0; i < n; i++)
            {
                // Matrix.Invert returns the inverse; safe even if bone is identity.
                invBindPose.Add(Matrix.Invert(abs[i]));
            }
            #endregion

            #region Build DNA Skeleton + Create Clip Dictionary

            // Build DNA Skeleton using the game's Bone.BuildSkeleton helper.
            Skeleton skel = Bone.BuildSkeleton(local, parents, names);

            // IMPORTANT:
            // We attach an EMPTY clip dictionary for now. This prevents null Skeleton crashes.
            // If the game expects animation clips for this asset, add clip extraction later.
            var clips = new Dictionary<string, AnimationClip>(StringComparer.OrdinalIgnoreCase);

            #endregion

            // Final: Attach all skinning metadata for runtime consumption.
            model.Tag = new SkinedAnimationData(clips, invBindPose, skel);
        }
        #endregion
    }
}
