/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using DNA.CastleMinerZ.Terrain;
using Microsoft.Xna.Framework;
using DNA.CastleMinerZ.AI;
using DNA.CastleMinerZ;
using DNA.Drawing;
using System;

using static ModLoader.LogSystem; // For Log(...).

namespace ModLoaderExt
{
    /// <summary>
    /// Hard entity cap for the active in-game Scene.
    ///
    /// Goal:
    /// - Enforce a max count by deleting entities over the limit (host-only with client fallback).
    ///
    /// Notes:
    /// - We only consider mainScene.Children (top-level). This avoids touching internal sub-entities
    ///   like player/model bones and keeps the logic safer.
    /// - Pickups are removed via PickupManager to keep internal lists consistent.
    /// </summary>
    internal static class EntityLimiterSystem
    {
        // ======================================================================================
        // Summary
        // ======================================================================================
        // This system runs while the player is in-game (GameScreen is active) and enforces a
        // hard cap on top-level scene entities:
        //   1) Collect eligible entities (top-level only).
        //   2) Rank by importance (DrawPriority), then distance to player.
        //   3) Delete everything beyond MaxGlobalEntities using type-appropriate removal paths.
        //
        // Design intent:
        // - "Hard cap" means entities are actually removed from the scene/manager lists, not merely hidden.
        // - Removal is performed via the owning manager when one exists (PickupManager / EnemyManager),
        //   and falls back to RemoveFromParent() only when necessary.
        // ======================================================================================

        #region Settings

        /// <summary>Enable/disable the entity cap.</summary>
        public static bool LimitEntities = true;

        /// <summary>Maximum number of (controlled) entities allowed in the scene.</summary>
        public static int MaxGlobalEntities = 500;

        #endregion

        #region Internals

        // --------------------------------------------------------------------------------------
        // Candidate record used for ranking:
        // - DrawPriority: Higher = more important (kept first).
        // - DistSq:       Aquared distance to player focus (kept nearer when priorities tie).
        // --------------------------------------------------------------------------------------
        private struct ScoredEntity
        {
            public Entity Entity;
            public float  DistSq;
            public int    DrawPriority;
        }

        // Scratch buffer to avoid per-tick allocations.
        private static readonly List<ScoredEntity> _candidates = new List<ScoredEntity>(1024);

        #endregion

        #region Public Tick

        /// <summary>
        /// Called by your patch/update hook while the game is running.
        ///
        /// Runtime flow:
        /// - Gate:    Only runs when GameScreen is the active top-level screen and mainScene is ready.
        /// - Execute: Collect/rank/trim entities according to MaxGlobalEntities.
        /// </summary>
        public static void Tick()
        {
            if (!LimitEntities)
                return;

            var game = CastleMinerZGame.Instance;
            var gs   = game?.GameScreen;

            // Not in a world / no game screen.
            if (gs == null)
                return;

            // Not the active top-level screen (front-end, menus, etc.).
            if (game.mainScreenGroup == null || game.mainScreenGroup.CurrentScreen != gs)
                return;

            // Scene not ready.
            if (gs.mainScene == null)
                return;

            try
            {
                Scene   scene = gs.mainScene;
                Vector3 focus = TryGetFocusPosition();

                ApplyPolicy(scene, focus);
            }
            catch (Exception ex)
            {
                Log($"[EntityLimiter] Tick failed: {ex.GetType().Name}: {ex.Message}.");
            }
        }
        #endregion

        #region Policy

        /// <summary>
        /// Main limiter policy:
        /// - Collect top-level entities that are eligible for control.
        /// - Sort candidates by priority then distance.
        /// - Remove entities beyond the cap using TryHardRemove.
        /// </summary>
        private static void ApplyPolicy(Scene scene, Vector3 focus)
        {
            int budget = Math.Max(0, MaxGlobalEntities);

            _candidates.Clear();
            CollectTopLevel(scene, focus);

            if (_candidates.Count <= budget)
                return;

            // Keep "more important" things first, then nearest.
            _candidates.Sort((a, b) =>
            {
                int pr = b.DrawPriority.CompareTo(a.DrawPriority);
                if (pr != 0) return pr;
                return a.DistSq.CompareTo(b.DistSq);
            });

            EnforceHardCap(_candidates, budget);
        }

        /// <summary>
        /// Deletes all candidates beyond maxKeep.
        /// Removal is done from the end of the list to keep RemoveAt(i) safe.
        /// </summary>
        private static void EnforceHardCap(List<ScoredEntity> sorted, int maxKeep)
        {
            if (sorted == null) return;

            maxKeep = Math.Max(0, maxKeep);
            if (sorted.Count <= maxKeep)
                return;

            // Remove from the end so RemoveAt(i) is safe.
            for (int i = sorted.Count - 1; i >= maxKeep; i--)
            {
                Entity e = sorted[i].Entity;

                // Remove from our local list first.
                sorted.RemoveAt(i);

                TryHardRemove(e);
            }
        }

        /// <summary>
        /// Type-aware removal:
        /// - Pickups:  Remove via PickupManager (prevents dangling manager references).
        /// - Enemies:  Remove via EnemyManager.RemoveZombie (keeps lists/counters correct).
        /// - Dragon:   Prefer EnemyManager.RemoveDragon in online games; fall back to RemoveDragonEntity.
        /// - Fallback: RemoveFromParent for anything else.
        /// </summary>
        private static void TryHardRemove(Entity e)
        {
            if (e == null) return;

            try
            {
                // ----------------------------------------------------------------------
                // Pickups: MUST go through PickupManager to avoid dangling references.
                // ----------------------------------------------------------------------
                if (e is PickupEntity pe)
                {
                    var pm = PickupManager.Instance;
                    if (pm != null)
                    {
                        pm.RemovePickup(pe);
                        return;
                    }

                    // Fallback.
                    pe.RemoveFromParent();
                    return;
                }

                // ----------------------------------------------------------------------
                // Enemies: Use EnemyManager.RemoveZombie(...) so internal lists/counters stay correct.
                // ----------------------------------------------------------------------
                if (e is BaseZombie z)
                {
                    var em = EnemyManager.Instance;

                    // Best-effort "despawn": Remove from manager list + scene.
                    // (This avoids pickups/stat credit that might happen in the normal kill pipeline.)
                    if (em != null)
                    {
                        em.RemoveZombie(z);
                        return;
                    }

                    // Fallback if manager isn't available.
                    z.RemoveFromParent();
                    return;
                }

                // ----------------------------------------------------------------------
                // Dragon: Remove via EnemyManager (online-safe if you send the message).
                // ----------------------------------------------------------------------
                if (e is DragonEntity || e is DragonClientEntity)
                {
                    var em = EnemyManager.Instance;
                    if (em != null)
                    {
                        // If you are host in an online game, prefer broadcasting the removal so clients match.
                        if (CastleMinerZGame.Instance != null && CastleMinerZGame.Instance.IsOnlineGame)
                        {
                            try
                            {
                                em.RemoveDragon(); // Sends RemoveDragonMessage -> calls RemoveDragonEntity on everyone.
                                return;
                            }
                            catch
                            {
                                // If messaging fails (no valid gamer/etc.), fall back to local removal.
                            }
                        }

                        em.RemoveDragonEntity();
                        return;
                    }

                    // Fallback.
                    e.RemoveFromParent();
                    return;
                }

                // ----------------------------------------------------------------------
                // Generic fallback: Remove from scene graph.
                // ----------------------------------------------------------------------
                e.RemoveFromParent();
            }
            catch
            {
                // Best-effort: Limiter should never crash the game.
            }
        }
        #endregion

        #region Collection / Filtering

        /// <summary>
        /// Collects only top-level entities from the active scene:
        /// - This intentionally avoids recursion into sub-entities (bones/attachments/etc.).
        /// - Each candidate is scored by distance to the player focus.
        /// </summary>
        private static void CollectTopLevel(Scene scene, Vector3 focus)
        {
            var children = scene.Children;
            if (children == null)
                return;

            for (int i = 0; i < children.Count; i++)
            {
                Entity e = children[i];
                if (e == null) continue;

                if (!ShouldControl(e))
                    continue;

                Vector3 d = e.WorldPosition - focus;

                _candidates.Add(new ScoredEntity
                {
                    Entity       = e,
                    DistSq       = d.LengthSquared(),
                    DrawPriority = e.DrawPriority
                });
            }
        }

        /// <summary>
        /// Hard exclusions for entities that should never be deleted by the limiter.
        /// Includes top-level terrain/player/sky and key game subsystems.
        ///
        /// Additional safety:
        /// - Avoid anything named "*Manager".
        /// - Avoid UI/Terrain namespaces if they appear as top-level entities.
        /// </summary>
        private static bool ShouldControl(Entity e)
        {
            if (e == null) return false;

            // Never touch these top-level roots/systems.
            if (e is BlockTerrain)       return false;
            if (e is Player)             return false;
            if (e is Selector)           return false;
            if (e is CrackBoxEntity)     return false;
            if (e is GPSMarkerEntity)    return false;
            if (e is GameMessageManager) return false;
            if (e is SkySphere)          return false; // Covers CastleMinerSky and any other sky sphere variants.

            // Don't delete managers (safer).
            var t = e.GetType();
            var ns = t.Namespace ?? string.Empty;
            var name = t.Name ?? string.Empty;

            if (name.EndsWith("Manager", StringComparison.Ordinal))
                return false;

            // Extra safety: don't touch UI / Terrain namespaces if they appear.
            if (ns.IndexOf(".UI",      StringComparison.Ordinal) >= 0) return false;
            if (ns.IndexOf(".Terrain", StringComparison.Ordinal) >= 0) return false;

            return true;
        }
        #endregion

        #region Focus

        /// <summary>
        /// Focus point for distance scoring (typically the local player's world position).
        /// If unavailable, returns Vector3.Zero as a safe fallback.
        /// </summary>
        private static Vector3 TryGetFocusPosition()
        {
            try
            {
                // Prefer the player's world position.
                if (CastleMinerZGame.Instance?.LocalPlayer is Entity lp)
                    return lp.WorldPosition;
            }
            catch { }

            return Vector3.Zero;
        }
        #endregion
    }
}