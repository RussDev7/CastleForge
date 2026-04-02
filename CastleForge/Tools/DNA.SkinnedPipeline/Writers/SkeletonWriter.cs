/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework.Content.Pipeline.Serialization.Compiler;
using Microsoft.Xna.Framework.Content.Pipeline;
using DNA.Drawing;

namespace CMZ.ContentPipeline
{
    /// <summary>
    /// =========================================================================================
    /// SkeletonWriter
    /// =========================================================================================
    ///
    /// Purpose
    /// -------
    /// Writes DNA.Drawing.Skeleton into XNB using the *exact* binary layout expected by the game's
    /// runtime reader:
    ///     DNA.Drawing.Skeleton+Reader
    ///
    /// Why this exists
    /// ---------------
    /// Skinned models in CMZ/DNA typically embed SkinedAnimationData that references a Skeleton.
    /// If XNA chooses a reflective reader or a mismatched layout, runtime loading may fail or
    /// produce invalid bone hierarchies/transforms.
    ///
    /// Layout Contract
    /// ---------------
    /// The writer emits data in the same order the runtime expects:
    ///   1) BoneCount (int)
    ///   2) For each bone:
    ///        - Name (string)
    ///        - ParentIndex (int)   (-1 if root/no parent)
    ///        - Transform (Matrix) (local/bind pose transform)
    ///
    /// Notes
    /// -----
    /// - Parent index is derived from b.Parent.Index and uses -1 for root bones.
    /// - Writer forces the runtime reader type via GetRuntimeReader() so the XNB manifest binds
    ///   to the game's existing registered reader.
    ///
    /// =========================================================================================
    /// </summary>
    [ContentTypeWriter]
    public sealed class SkeletonWriter : ContentTypeWriter<Skeleton>
    {
        #region ContentTypeWriter Overrides

        /// <summary>
        /// Serialize a Skeleton into XNB in the exact field order expected by the runtime reader.
        /// </summary>
        protected override void Write(ContentWriter output, Skeleton value)
        {
            int count = value.Count;
            output.Write(count);

            for (int i = 0; i < count; i++)
            {
                var b = value[i];

                output.Write(b.Name ?? "");
                output.Write(b.Parent == null ? -1 : b.Parent.Index);
                output.Write(b.Transform);
            }
        }

        /// <summary>
        /// Forces the XNB manifest to bind to the game's existing runtime reader type.
        /// </summary>
        public override string GetRuntimeReader(TargetPlatform targetPlatform)
        {
            // Nested reader type: DNA.Drawing.Skeleton+Reader
            return typeof(Skeleton.Reader).AssemblyQualifiedName;
        }
        #endregion
    }
}