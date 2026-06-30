/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework.Content.Pipeline.Processors;
using Microsoft.Xna.Framework.Content.Pipeline.Graphics;
using Microsoft.Xna.Framework.Content.Pipeline;

namespace CMZ.ContentPipeline
{
    /// <summary>
    /// ModelProcessor wrapper for CastleMiner Z rigid/item models.
    ///
    /// The inherited ModelProcessor.Scale property still handles normal mesh geometry.
    /// This wrapper also fixes transform-only socket nodes such as BarrelTip, which the game
    /// reads directly for muzzle flashes/projectile origins and orientation.
    /// </summary>
    [ContentProcessor(DisplayName = "ScaledModelProcessor")]
    public sealed class ScaledModelProcessor : ModelProcessor
    {
        /// <summary>
        /// Comma/semicolon/pipe-separated socket/helper bone names that should receive the
        /// round-trip scale correction. Defaults to common CMZ sockets.
        /// </summary>
        public string SocketNodeNames { get; set; } = ProcessorScaleUtility.DefaultSocketNodeNames;

        /// <summary>
        /// If true, named socket nodes are moved directly under the imported model root before
        /// ModelProcessor runs, preserving their model-space transform.
        ///
        /// This is enabled for rigid models because CMZ reads BarrelTip.Transform directly.
        /// Without this, a Blender/FBX parent such as "Gun Test" can leave BarrelTip stored in
        /// parent/local space, which places the muzzle flash left/back near the trigger.
        /// </summary>
        public bool SocketBakeToModelRoot { get; set; } = true;

        /// <summary>
        /// Comma/semicolon/pipe-separated socket names to bake to model-root space.
        /// Defaults to BarrelTip only.
        /// </summary>
        public string SocketBakeToModelRootNames { get; set; } = ProcessorScaleUtility.DefaultSocketBakeToModelRootNames;

        /// <summary>
        /// Writes socket bake details to the content pipeline log.
        /// </summary>
        public bool SocketDebugLog { get; set; } = false;

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

        /// <summary>
        /// Applies post-ModelProcessor socket basis/orientation correction.
        ///
        /// Keep this enabled by default, but pair it with SocketTranslationScale=1.0.
        /// XNA ModelProcessor.Scale already scales BarrelTip translation; this post pass scales
        /// the socket basis so muzzle flashes do not inherit FBX centimeter-scale axes.
        /// </summary>
        public bool SocketPostProcessCorrection { get; set; } = true;

        /// <summary>
        /// Basis scale for socket/helper bones after ModelProcessor.
        ///
        /// Blender FBX exports socket axes at centimeter scale (about 100). ModelProcessor.Scale
        /// scales the socket translation but leaves those axes large. A basis scale of 0.01
        /// brings the socket basis back to normal size without changing placement.
        /// </summary>
        public float SocketBasisScale { get; set; } = ProcessorScaleUtility.DefaultSocketBasisScale;

        /// <summary>
        /// Optional basis remap mode for Blender GLB -> FBX round-trips.
        ///
        /// Use BlenderGlbRoundTrip when a socket imported from a TexturePacks GLB is exported
        /// through Blender FBX and XNA receives the basis transposed/remapped.
        /// </summary>
        public string SocketBasisTransform { get; set; } = ProcessorScaleUtility.DefaultSocketBasisTransform;

        /// <summary>
        /// Translation scale for socket/helper bones.
        /// A value <= 0 means use the inherited ModelProcessor Scale value.
        ///
        /// For rigid TexturePacks round-trips, leave this at 1.0. XNA ModelProcessor.Scale
        /// already scales the processed BarrelTip translation, but it leaves the socket basis
        /// at FBX centimeter scale. Scaling translation again collapses the muzzle back near
        /// the model origin/trigger.
        /// </summary>
        public float SocketTranslationScale { get; set; } = 1f;

        /// <summary>
        /// Optional BarrelTip basis correction. Disabled by default because the normal fix is
        /// root-space baking plus scale compensation, not a hardcoded rotation flip.
        /// </summary>
        public bool SocketRotationCorrection { get; set; } = false;

        /// <summary>
        /// Comma/semicolon/pipe-separated socket names that should receive the optional basis correction.
        /// Defaults to BarrelTip only. Flame is scaled but not rotated.
        /// </summary>
        public string SocketRotationCorrectionNames { get; set; } = ProcessorScaleUtility.DefaultSocketRotationCorrectionNames;

        /// <summary>
        /// Axis used for the optional socket-only basis correction. Valid values: X, Y, Z.
        /// </summary>
        public string SocketRotationCorrectionAxis { get; set; } = ProcessorScaleUtility.DefaultSocketRotationCorrectionAxis;

        /// <summary>
        /// Degrees used for the optional socket-only basis correction.
        /// </summary>
        public float SocketRotationCorrectionDegrees { get; set; } = ProcessorScaleUtility.DefaultSocketRotationCorrectionDegrees;

        public override ModelContent Process(NodeContent input, ContentProcessorContext context)
        {
            bool hasFullRigidRestore = ProcessorScaleUtility.HasRigidNodeTransformRestoreFile(RigidMeshRestoreFile);

            if (hasFullRigidRestore)
            {
                // The full sidecar already contains the original rigid node transforms.
                // Do not also inject the inverse RootNode authoring transform; that would
                // offset sockets like BarrelTip and can leave the visible weapon rotated in
                // the clean Blender authoring frame.
                ProcessorScaleUtility.ResetAuthoringRootTransforms(input, context, SocketDebugLog);

                ProcessorScaleUtility.ApplyRigidMeshRotationRestore(
                    input,
                    RigidMeshRestoreFile,
                    AuthoringLocationScale,
                    AuthoringLocation,
                    AuthoringRotation,
                    AuthoringRotationQuaternion,
                    AuthoringLocationScale,
                    context,
                    SocketDebugLog);
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
                    SocketDebugLog);

                ProcessorScaleUtility.ApplyRigidMeshRotationRestore(
                    input,
                    RigidMeshRestoreFile,
                    AuthoringLocationScale,
                    AuthoringLocation,
                    AuthoringRotation,
                    AuthoringRotationQuaternion,
                    AuthoringLocationScale,
                    context,
                    SocketDebugLog);
            }

            if (SocketDebugLog)
            {
                ProcessorScaleUtility.LogMatchingInputNodes(
                    input,
                    SocketNodeNames,
                    context,
                    "BeforeBake");
            }

            if (SocketBakeToModelRoot)
            {
                ProcessorScaleUtility.BakeNamedSocketNodesToModelRoot(
                    input,
                    SocketBakeToModelRootNames,
                    context,
                    SocketDebugLog);
            }

            var model = base.Process(input, context);

            if (SocketDebugLog)
            {
                ProcessorScaleUtility.LogMatchingModelBones(
                    model,
                    SocketNodeNames,
                    context,
                    "AfterModelProcessor");
            }

            if (SocketPostProcessCorrection)
            {
                ProcessorScaleUtility.FixNamedSocketBones(
                    model,
                    socketBasisScale: SocketBasisScale,
                    socketTranslationScale: SocketTranslationScale,
                    socketNodeNames: SocketNodeNames,
                    enableSocketRotationCorrection: SocketRotationCorrection,
                    socketRotationCorrectionNames: SocketRotationCorrectionNames,
                    socketRotationCorrectionAxis: SocketRotationCorrectionAxis,
                    socketRotationCorrectionDegrees: SocketRotationCorrectionDegrees,
                    socketBasisTransform: SocketBasisTransform);

                if (SocketDebugLog)
                {
                    ProcessorScaleUtility.LogMatchingModelBones(
                        model,
                        SocketNodeNames,
                        context,
                        "AfterSocketPostProcess");
                }
            }

            return model;
        }
    }
}
