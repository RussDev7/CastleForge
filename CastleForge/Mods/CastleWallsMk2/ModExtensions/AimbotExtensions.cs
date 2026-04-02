/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using DNA.CastleMinerZ.Inventory;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using DNA.Net.GamerServices;
using DNA.CastleMinerZ.Net;
using DNA.CastleMinerZ.AI;
using DNA.CastleMinerZ.UI;
using DNA.CastleMinerZ;
using DNA.Drawing;
using HarmonyLib;
using System;
using DNA;

namespace CastleWallsMk2
{
    #region AimTargeting

    internal static class AimTargeting
    {
        #region Public API

        #region Closest Enabled Target

        /// <summary>
        /// Find the closest valid target among the enabled categories.
        /// Returns true if a target was found, along with its entity and (local) AABB for LOS checks.
        /// </summary>
        public static bool TryFindClosestEnabledTarget(
            bool includePlayers,
            bool includeMobs,
            bool includeDragons,
            bool requireVisible,
            out Entity target,
            out BoundingBox? localAabbForLos)
        {
            target          = null;
            localAabbForLos = null;

            var game  = CastleMinerZGame.Instance;
            var local = game?.LocalPlayer;
            if (game?.CurrentNetworkSession == null || local == null) return false;
            if (local.Dead) return false; // Ensure we are alive.

            // Keep best results in locals (NOT the out params).
            Entity       bestTarget = null;
            BoundingBox? bestAabb   = null;
            float        bestD2     = float.MaxValue;

            // Helper: Evaluate a candidate and update the local "best" fields.
            bool Consider(Entity candidate, BoundingBox candLocalAabb)
            {
                // Skip if we are dead; Or this enemy is null/local; Or tag not a live/visible Enemy.
                if (candidate == null) return false;

                // Basic validity by type.
                if (candidate is Player player)
                {
                    if (player.Dead || !player.Visible) return false;
                }
                else if (candidate is BaseZombie zombie)
                {
                    if (zombie.IsDead || !zombie.Visible) return false;

                    // Skip emerge, give up, and death animiations.
                    var animiation = zombie.CurrentPlayer?.Name;
                    if (animiation != null                                                     &&

                        // Emerge //
                        animiation.StartsWith("jump",      StringComparison.OrdinalIgnoreCase) || // Alien.
                        animiation.StartsWith("idle",      StringComparison.OrdinalIgnoreCase) || // Felguard.
                        animiation.StartsWith("standup",   StringComparison.OrdinalIgnoreCase) || // Skeleton.
                        animiation.StartsWith("arise",     StringComparison.OrdinalIgnoreCase) || // Zombie.

                        // GiveUp //
                        animiation.StartsWith("jump",      StringComparison.OrdinalIgnoreCase) || // Alien.
                        animiation.StartsWith("idle",      StringComparison.OrdinalIgnoreCase) || // Felguard.
                        animiation.StartsWith("enraged",   StringComparison.OrdinalIgnoreCase) || // Skeleton.
                        animiation.StartsWith("eat_start", StringComparison.OrdinalIgnoreCase) || // Zombie.

                        // Death //
                        animiation.StartsWith("death",     StringComparison.OrdinalIgnoreCase)    // All enemies.
                    ) return false;
                }
                else if (candidate is DragonClientEntity dragon)
                {
                    if (dragon.Dead) return false;
                }

                // Optional terrain LOS check.
                if (requireVisible && !HasLineOfSightTo(candidate, candLocalAabb, eyeYBiasBlocks: 0.5f))
                    return false;

                // Compare squared distance (cheaper than Distance()).
                float distanceSquared = Vector3.DistanceSquared(local.WorldPosition, candidate.WorldPosition);
                if (distanceSquared < bestD2)
                {
                    bestD2     = distanceSquared;
                    bestAabb   = candLocalAabb;
                    bestTarget = candidate;
                    return true;
                }
                return false;
            }

            // Players.
            if (includePlayers)
            {
                foreach (var g in game.CurrentNetworkSession.AllGamers)
                {
                    if (g == null || g.IsLocal) continue;
                    if (g.Tag is Player ep)
                    {
                        Consider(ep, ep.PlayerAABB);
                    }
                }
            }

            // Mobs.
            if (includeMobs && EnemyManager.Instance != null)
            {
                var listRef = AccessTools.FieldRefAccess<EnemyManager, List<BaseZombie>>("_enemies");
                foreach (var z in listRef(EnemyManager.Instance).ToArray())
                {
                    if (z == null) continue;
                    Consider(z, z.PlayerAABB);
                }
            }

            // Dragon.
            if (includeDragons && EnemyManager.Instance != null)
            {
                var dragon = AccessTools.FieldRefAccess<EnemyManager, DragonClientEntity>("_dragonClient")(EnemyManager.Instance);
                if (dragon != null)
                {
                    var aabbLocal = dragon.GetAABB(); // Local-space AABB.
                    Consider(dragon, aabbLocal);
                }
            }

            // Assign to the out params only once, outside of the local function.
            target          = bestTarget;
            localAabbForLos = bestAabb;
            return target != null;
        }
        #endregion

        #region UNUSED: Obsolete

        #region Closest Player

        /// <summary>
        /// Find the nearest valid non-local player to aim at.
        /// Optionally requires a line-of-sight test (terrain only).
        /// </summary>
        public static NetworkGamer FindClosestValidPlayer(bool requireVisible)
        {
            var game  = CastleMinerZGame.Instance;
            var local = game?.LocalPlayer;
            if (game?.CurrentNetworkSession == null || local == null) return null;

            NetworkGamer best = null;
            float bestD2 = float.MaxValue;

            foreach (var gamer in game.CurrentNetworkSession.AllGamers)
            {
                // Skip if we are dead; Or this gamer is null/local; Or tag not a live/visible Player.
                if (local.Dead) continue;
                if (gamer == null || gamer.IsLocal) continue;
                if (!(gamer.Tag is Player enemyPlayer) || enemyPlayer.Dead || !enemyPlayer.Visible) continue;

                // Optional terrain LOS check.
                if (requireVisible && !HasLineOfSightTo(enemyPlayer, enemyPlayer.PlayerAABB, eyeYBiasBlocks: 0.5f)) continue;

                // Compare squared distance (cheaper than Distance()).
                float d2 = Vector3.DistanceSquared(local.LocalPosition, enemyPlayer.LocalPosition);
                if (d2 < bestD2) { bestD2 = d2; best = gamer; }
            }
            return best;
        }
        #endregion

        #region Closest Mob

        /// <summary>
        /// Finds the closest valid mob as an Entity.
        /// - Filters null/hidden/dead entries defensively.
        /// - Filters invulnerable animation states.
        /// - Prefers world-space distance from the camera.
        /// </summary>
        public static BaseZombie FindClosestValidMob(bool requireVisible)
        {
            var game = CastleMinerZGame.Instance;
            var local = game?.LocalPlayer;
            if (game?.CurrentNetworkSession == null || local == null || EnemyManager.Instance == null) return null;

            BaseZombie best = null;
            float bestD2 = float.MaxValue;

            // Iterate through the typed accessor of EnemyManager._enemies.
            foreach (var zombie in AccessTools.FieldRefAccess<EnemyManager, List<BaseZombie>>("_enemies")(EnemyManager.Instance).ToArray())
            {
                // Skip if we are dead; Or this enemy is null/local; Or tag not a live/visible Enemy.
                if (local.Dead) continue;
                if (zombie == null) continue;
                if (!(zombie is BaseZombie enemyEntity) || !zombie.Visible || zombie.IsDead) continue;

                // Skip emerge, give up, and death animiations.
                var animiation = zombie.CurrentPlayer?.Name;
                if (animiation != null                                                     &&

                    // Emerge //
                    animiation.StartsWith("jump",      StringComparison.OrdinalIgnoreCase) || // Alien.
                    animiation.StartsWith("idle",      StringComparison.OrdinalIgnoreCase) || // Felguard.
                    animiation.StartsWith("standup",   StringComparison.OrdinalIgnoreCase) || // Skeleton.
                    animiation.StartsWith("arise",     StringComparison.OrdinalIgnoreCase) || // Zombie.

                    // GiveUp //
                    animiation.StartsWith("jump",      StringComparison.OrdinalIgnoreCase) || // Alien.
                    animiation.StartsWith("idle",      StringComparison.OrdinalIgnoreCase) || // Felguard.
                    animiation.StartsWith("enraged",   StringComparison.OrdinalIgnoreCase) || // Skeleton.
                    animiation.StartsWith("eat_start", StringComparison.OrdinalIgnoreCase) || // Zombie.

                    // Death //
                    animiation.StartsWith("death",     StringComparison.OrdinalIgnoreCase)    // All enemies.
                ) continue;

                // Optional terrain LOS check.
                if (requireVisible && !HasLineOfSightTo(zombie, zombie.PlayerAABB, eyeYBiasBlocks: 0.5f)) continue;

                // Compare squared distance (cheaper than Distance()).
                float d2 = Vector3.DistanceSquared(local.WorldPosition, enemyEntity.WorldPosition);
                if (d2 < bestD2) { bestD2 = d2; best = enemyEntity; }
            }
            return best;
        }
        #endregion

        #region Closest Dragon

        /// <summary>
        /// Finds the closest valid dragon as an Entity.
        /// - Filters null/hidden/dead entries defensively.
        /// </summary>
        public static DragonEntity FindClosestValidDragon(bool requireVisible)
        {
            var game = CastleMinerZGame.Instance;
            var local = game?.LocalPlayer;
            if (game?.CurrentNetworkSession == null || local == null) return null;

            // Grab the typed accessor of EnemyManager._dragon.
            DragonEntity dragon = AccessTools.FieldRefAccess<EnemyManager, DragonEntity>("_dragon")(EnemyManager.Instance);

            // Skip if we are dead; Or this enemy is null/local; Or tag not a live/visible Enemy.
            if (local.Dead) return null;
            if (dragon == null) return null;
            if (!(dragon is DragonEntity enemyEntity) || dragon.Removed) return null;

            // Optional terrain LOS check.
            if (requireVisible && !HasLineOfSightTo(enemyEntity, dragon.GetAABB(), eyeYBiasBlocks: 0.5f)) return null;

            return dragon;
        }
        #endregion

        #endregion

        #endregion

        #region Line of Sight

        /// <summary>
        /// Terrain-only line of sight test using the game's ConstructionProbe.
        /// Succeeds if ANY of several target sample points are unblocked.
        ///
        /// eyeYBiasBlocks:    Raise/lower the ray start (camera). +up / -down.
        /// targetYBiasBlocks: Raise/lower the ray end on the target AABB.
        /// </summary>
        public static bool HasLineOfSightTo(Entity target, BoundingBox playerAABB, float eyeYBiasBlocks = 0.0f, float targetYBiasBlocks = 0.0f)
        {
            var game = CastleMinerZGame.Instance;
            var hud  = InGameHUD.Instance;
            if (game?.LocalPlayer?.FPSCamera == null || target == null || hud == null) return false;

            // Start from camera (optionally offset vertically).
            Vector3 eye = game.LocalPlayer.FPSCamera.WorldPosition + new Vector3(0f, eyeYBiasBlocks, 0f);

            // Compute the target's world-space AABB.
            BoundingBox bb = playerAABB;
            Vector3 tp = target.WorldPosition;
            bb.Min += tp; bb.Max += tp;

            // Offset sample points vertically.
            Vector3 bias = new Vector3(0f, targetYBiasBlocks, 0f);

            // Sample three points across the target's height.
            Vector3 center = (bb.Min + bb.Max) * 0.5f;
            float h = bb.Max.Y - bb.Min.Y;

            Vector3 head   = new Vector3(center.X, bb.Max.Y - 0.10f,     center.Z) + bias;
            Vector3 chest  = new Vector3(center.X, bb.Min.Y + 0.60f * h, center.Z) + bias;
            Vector3 pelvis = new Vector3(center.X, bb.Min.Y + 0.35f * h, center.Z) + bias;

            // Visible if any ray is unblocked.
            return RayUnblocked(eye, head, hud)  ||
                   RayUnblocked(eye, chest, hud) ||
                   RayUnblocked(eye, pelvis, hud);
        }
        #endregion

        #region Public Helpers

        #region AimPoint & AABB

        /// <summary>
        /// Compute a world-space aim point and (when available) a world-space AABB for a target entity.
        ///
        /// Why:
        /// - Aimbot math often wants a single "aim point" (usually center-mass or head-ish).
        /// - LOS / visibility checks benefit from a bounding box to sample multiple points.
        ///
        /// Conventions:
        /// - <paramref name="offset"/> is applied in WORLD space (e.g., +Y ≈ head height).
        /// - <paramref name="worldAabb"/> is returned in WORLD space (Min/Max already translated).
        /// - Returns false only when <paramref name="target"/> is null or its position cannot be read.
        ///
        /// Notes:
        /// - Players and Zombies supply a PlayerAABB in LOCAL space, so we translate by WorldPosition.
        /// - Dragons don't expose a "PlayerAABB"; many builds have GetAABB() (local-space).
        ///   We translate that to world and return it when available; otherwise we at least return
        ///   a reasonable aim point.
        /// - If the target type is unknown, we still try to produce an aim point and set AABB = null.
        ///
        /// Typical usage:
        ///   if (TryGetAimPointAndAABB(target, new Vector3(0, 1.2f, 0), out var pt, out var wbb)) {
        ///       // Use pt for aim math (AimMath / Ballistics).
        ///       // Use wbb (if not null) for LOS probes or on-screen boxes.
        ///   }
        /// </summary>
        public static bool TryGetAimPointAndAABB(Entity target, Vector3 offset, out Vector3 aimPoint, out BoundingBox? worldAabb)
        {
            aimPoint  = default;
            worldAabb = null;

            if (target == null)
                return false;

            // Players //
            // PlayerAABB is in LOCAL space; translate to WORLD by adding WorldPosition.
            if (target is Player p)
            {
                var bb = p.PlayerAABB;
                var wp = p.WorldPosition;   // World origin of the entity.
                bb.Min += wp; bb.Max += wp; // LOCAL -> WORLD.
                worldAabb = bb;

                // Aim at center-mass by default, then apply caller's world-space offset.
                aimPoint = ((bb.Min + bb.Max) * 0.5f) + offset;
                return true;
            }

            // Zombies (all mobs) //
            if (target is BaseZombie z)
            {
                var bb    = z.PlayerAABB;
                var wp    = z.WorldPosition;
                bb.Min += wp; bb.Max += wp;
                worldAabb = bb;

                aimPoint = ((bb.Min + bb.Max) * 0.5f) + offset;
                return true;
            }

            // Dragons //
            // Many builds expose DragonEntity.GetAABB() returning a LOCAL-space box.
            // We try to use it; if not present, we still return a good aim point.
            if (target is DragonEntity d)
            {
                // Best-effort AABB (older builds may throw if GetAABB is absent/different).
                try
                {
                    var localBb = d.GetAABB(); // LOCAL-space in most CMZ builds.
                    var wp      = d.WorldPosition;
                    localBb.Min += wp; localBb.Max += wp;
                    worldAabb   = localBb;

                    aimPoint = ((localBb.Min + localBb.Max) * 0.5f) + offset;
                    return true;
                }
                catch
                {
                    // Fallback: No AABB, but keep a meaningful aim point (e.g., center of body).
                    aimPoint = d.WorldPosition + offset;
                    return true;
                }
            }

            // Fallback / Unknown types //
            // For any other Entity, at least try to aim at its world origin + offset.
            // (Bounding box unknown -> keep null; callers should handle that.)
            try
            {
                aimPoint = target.WorldPosition + offset;
                return true;
            }
            catch
            {
                // If even WorldPosition isn't readable (unlikely), signal failure.
                return false;
            }
        }
        #endregion

        #endregion

        #region Private Helpers

        /// <summary>
        /// Casts a single probe from start->end against terrain.
        /// Returns true if no collision with blocks along the path.
        /// </summary>
        private static bool RayUnblocked(Vector3 start, Vector3 end, InGameHUD hud)
        {
            // Normalize direction and nudge start forward slightly to avoid "inside block" starts.
            Vector3 d = end - start;
            float len2 = d.LengthSquared();
            if (len2 < 1e-6f) return true;           // Same point -> Trivially visible.
            d *= 1f / (float)Math.Sqrt(len2);
            Vector3 s = start + d * 0.05f;

            var probe = hud.ConstructionProbe;
            probe.Init(s, end, checkEnemies: false); // terrain only.
            probe.SkipEmbedded      = true;          // ignore if we started embedded.
            probe.TraceCompletePath = false;         // first collision is enough.
            probe.Trace();

            // NOTE: AbleToBuild == "hit something interactable (blocked)".
            // For visibility we want the inverse: true when nothing was hit.
            return !probe.AbleToBuild;
        }
        #endregion
    }
    #endregion

    #region AimbotController

    internal static class AimbotController
    {
        #region State

        public static bool    ShowTargetCrosshair;                   // UI toggle (draw a crosshair/indicator for current target).
        public static bool    FaceTorwardsEnemy;                     // If true, a Harmony patch will rotate the camera/player toward Target each frame.
        public static bool    IsShouldered;                          // When true, treat the aimbot shot as "aim-down-sights" (ADS).
        public static Entity  Target;                                // The entity we're trying to aim at (usually a Player).
        public static float   LeadSpeed = 0.0f;                      // Simple linear lead: target velocity * LeadSpeed (0 = no lead).
        public static Vector3 Offset    = new Vector3(0f, 1.2f, 0f); // Where to aim on the target relative to its origin (default ~head height).
        public static Angle   Roll      = Angle.Zero;                // Optional roll around the forward axis when we build the aim matrix.
        #endregion

        #region API

        /// <summary>Reset aimbot state (called when releasing the hotkey, etc.).</summary>
        public static void Clear()
        {
            ShowTargetCrosshair = false;
            FaceTorwardsEnemy   = false;
            IsShouldered        = false;
            Target              = null;
            LeadSpeed           = 0f;
            Offset              = new Vector3(0f, 1.6f, 0f);
            Roll                = Angle.Zero;
        }
        #endregion
    }
    #endregion

    #region AimMath

    internal static class AimMath
    {
        #region Public API

        /// <summary>
        /// Build a LocalToWorld matrix that "looks" from the camera to the target,
        /// applying an offset and simple velocity lead.
        /// Returns false if input is invalid or vectors degenerate.
        /// </summary>
        public static bool TryMakeAimL2W(
            PerspectiveCamera cam,
            Entity target,
            Vector3 offset,
            float   leadSpeed,
            Angle   roll,
            out Matrix aimL2W)
        {
            aimL2W = default;
            if (cam == null || target == null) return false;
            if (target is Player tp && (!tp.Visible || tp.Dead)) return false;

            // Eye position from camera; Target prediction by velocity*lead.
            Vector3 eye  = cam.WorldPosition;
            var phys     = target.Physics as BasicPhysics;
            Vector3 lead = (phys?.WorldVelocity ?? Vector3.Zero) * leadSpeed;

            // Build forward vector toward the aim point.
            Vector3 at = target.WorldPosition + offset + lead;
            Vector3 f  = at - eye;
            if (f.LengthSquared() < 1e-6f) return false;
            f.Normalize();

            // Choose a stable up vector; Avoid near-parallel with forward.
            Vector3 up = Vector3.Up;
            if (Math.Abs(Vector3.Dot(f, up)) > 0.999f) up = Vector3.Right;

            // Optional roll around "f".
            if (roll != Angle.Zero)
                up = Vector3.Normalize(Vector3.TransformNormal(up, Matrix.CreateFromAxisAngle(f, roll.Radians)));

            // Output camera basis matrix.
            aimL2W = Matrix.CreateWorld(eye, f, up);
            return true;
        }
        #endregion
    }
    #endregion

    #region AimbotBallistics - Ballistic Drop & Lead

    /// <summary>
    /// Purpose:
    /// - Provide gravity/drop compensation for bullet weapons (not lasers/rockets).
    /// - Provide a low-arc aim solution for grenades using their vanilla throw speed and gravity.
    /// Notes:
    /// - Units follow the game's conventions: Positions in "blocks"; velocities in blocks/sec.
    /// - Gravity magnitudes taken from the engine update loops (see comments below).
    /// - We intentionally use the *original* (factory) muzzle velocity even when "super stats"
    ///   are enabled, so ballistic compensation remains realistic.
    /// </summary>
    internal static class AimbotBallistics
    {
        // Engine constants //
        private const float Gun_Gravity     = 10f;  // From Tracer.Update:                Tail/HeadVelocity.Y -= 10f * dt.
        private const float Grenade_Gravity = 9.8f; // From GrenadeProjectile.OnUpdate:   _linearVelocity += Vector3.Down * (9.8f * dt).
        private const float Velocity_THROW  = 15f;  // From Player.ProcessGrenadeMessage: frm.Direction * 15f.

        #region Bullet - Drop Gating

        /// <summary>
        /// Decide if we should apply ballistic correction for the currently held item.
        /// Guards:
        /// - User toggle must be ON.
        /// - Item must exist and be a gravity-affected firearm (not lasers/rockets/grenades).
        /// </summary>
        public static bool ShouldApplyBulletDrop(InventoryItem item, bool adjustForBulletDrop)
        {
            // Global user toggle.
            if (!adjustForBulletDrop) return false;
            if (item == null) return false;

            // Defensive null (old builds can hand you a null ItemClass).
            if (item.ItemClass == null) return false;

            // Families that do not use ballistic drop:
            if (item.ItemClass is LaserGunInventoryItemClass)             return false; // Handled as hitscan-like in BlasterShot.
            if (item.ItemClass is RocketLauncherInventoryItemClass)       return false; // Separate projectile system.
            if (item.ItemClass is RocketLauncherGuidedInventoryItemClass) return false;
            if (item.ItemClass is GrenadeInventoryItemClass)              return false;

            // Everything else (rifles/SMG/pistol/shotgun) -> Eligible.
            return true;
        }
        #endregion

        #region Firearm Ballistics (Low-Arc)

        // NOTE:
        // - Uses original muzzle speed via WeaponExtensions.GetOriginalVelocityOr so "super stats"
        //   don't break compensation.
        // - Subtracts the engine's +0.015 rad up-tilt (if enabled) to avoid double compensation.

        /// <summary>
        /// Build a LocalToWorld that aims with gravity compensation for firearms.
        /// Implementation:
        /// - Uses the closed-form (low-arc) solution for a projectile launched at speed v with gravity g.
        /// - Falls back to flat aim if target is unreachable (discriminant < 0) or degenerate distances.
        /// - Subtracts the engine's +0.015 rad "drop kick" that gets added at send time when enabled,
        ///   to avoid double-compensation (see GunshotMessage.Send).
        /// </summary>
        public static bool TryMakeAimL2W_WithBallistics(
            PerspectiveCamera     camera,
            Entity                target,
            Vector3               offset,
            GunInventoryItemClass gun,          // Provides Velocity & (via reflection) NeedsDropCompensation.
            out Matrix            localToWorld)
        {
            localToWorld = default;
            if (camera == null || target == null || gun == null) return false;

            // Lasers: BlasterShot has no drop - keep aim flat.
            if (gun is LaserGunInventoryItemClass)
                return AimFlat(camera, target, offset, out localToWorld);

            // World-space eye and aim point (with caller-provided per-target offset).
            Vector3 eyePosition = camera.WorldPosition;
            Vector3 aimPosition = target.WorldPosition + offset;

            // XZ planar direction & distance to target (used in the analytic formula).
            Vector3 directionXZ = new Vector3(aimPosition.X - eyePosition.X, 0f, aimPosition.Z - eyePosition.Z);
            float   distanceXZ  = directionXZ.Length();
            if (distanceXZ < 1e-3f)
                return AimFlat(camera, target, offset, out localToWorld); // Too close/degenerate.
            directionXZ /= distanceXZ;                                    // Normalize.

            // Vertical separation.
            float deltaY = (aimPosition.Y - eyePosition.Y);

            // Use the original muzzle speed so super-stats don't kill the compensation.
            // (Requires WeaponExtensions.GetOriginalVelocityOr to be available.)
            float muzzleSpeed = WeaponExtensions.GetOriginalVelocityOr(gun, gun.Velocity);
            muzzleSpeed = Math.Max(1e-3f, muzzleSpeed); // Safety.

            // Analytic low-arc solution:
            // tan(angle) = (v^2 - sqrt(v^4 - g*(g*d^2 + 2*dy*v^2))) / (g*d).
            double muzzleSpeedSquared = muzzleSpeed * muzzleSpeed;
            double muzzleSpeedFourth  = muzzleSpeedSquared * muzzleSpeedSquared;
            double discriminant       = muzzleSpeedFourth - Gun_Gravity * (Gun_Gravity * distanceXZ * distanceXZ + 2.0 * deltaY * muzzleSpeedSquared);

            // Unreachable with given speed: Fall back to flat aim.
            if (discriminant < 0.0)
                return AimFlat(camera, target, offset, out localToWorld);

            double sqrtDiscriminant = Math.Sqrt(Math.Max(0.0, discriminant));
            double tanAngle         = (muzzleSpeedSquared - sqrtDiscriminant) / (Gun_Gravity * distanceXZ);

            // Convert to pitch and clamp to a sane range to avoid extreme tilts.
            double angle = Math.Atan(tanAngle);
            angle = MathHelper.Clamp((float)angle, -0.5f, 0.5f); // ~±28.6°.

            // Engine "drop kick": GunshotMessage.Send adds +0.015 rad upward when enabled.
            // Subtract here if present to avoid double-compensation.
            if (NeedsDropCompensation(gun))
                angle -= 0.015; // radians

            // Build forward vector from planar dir and compensated pitch:
            // forward = cos(angle)*directionXZ + sin(angle)*Up.
            float   cosAngle = (float)Math.Cos(angle);
            float   sinAngle = (float)Math.Sin(angle);
            Vector3 forward  = Vector3.Normalize(directionXZ * cosAngle + Vector3.Up * sinAngle);

            // Pick a stable Up (avoid near-parallel to forward).
            Vector3 up = Vector3.Up;
            if (Math.Abs(Vector3.Dot(forward, up)) > 0.999f) up = Vector3.Right;

            localToWorld = Matrix.CreateWorld(eyePosition, forward, up);
            return true;
        }
        #endregion

        #region Grenade Ballistics (Max-Range Fallback)

        /// <summary>
        /// Build a LocalToWorld that aims a grenade with ballistic compensation.
        /// - If target is reachable with current throw speed/gravity, use the low-arc analytic solution.
        /// - If not reachable (discriminant < 0), aim for the furthest possible throw:
        ///   yaw toward target, pitch ≈ 45° (with a small optional bias for large height deltas).
        /// </summary>
        public static bool TryMakeAimL2W_Grenade(
            PerspectiveCamera camera,
            Entity            target,
            Vector3           offset,
            out Matrix        localToWorld)
        {
            localToWorld = default;
            if (camera == null || target == null) return false;

            // Eye (throw) position and target aim point.
            Vector3 eyePosition = camera.WorldPosition;
            Vector3 aimPosition = target.WorldPosition + offset;

            // Horizontal direction (XZ) toward the target.
            Vector3 directionXZ = new Vector3(aimPosition.X - eyePosition.X, 0f, aimPosition.Z - eyePosition.Z);
            float  distanceXZ   = directionXZ.Length();
            if (distanceXZ < 1e-4f) return false; // Degenerate.
            directionXZ /= distanceXZ;

            // Vertical delta (target height relative to eye).
            float deltaY = aimPosition.Y - eyePosition.Y;

            // Throw parameters.
            double throwSpeed         = Velocity_THROW;
            double throwSpeedSquared  = throwSpeed * throwSpeed;
            double throwSpeedFourth   = throwSpeedSquared * throwSpeedSquared;

            double angle; // Elevation angle in radians.

            // Projectile discriminant (same form as bullets, but with grenade constants).
            double discriminant = throwSpeedFourth
                                - Grenade_Gravity * (Grenade_Gravity * distanceXZ * distanceXZ + 2.0 * deltaY * throwSpeedSquared);

            if (discriminant >= 0.0)
            {
                // Reachable: Use the low arc (minus root).
                double sqrtDiscriminant = Math.Sqrt(discriminant);
                double tanAngle         = (throwSpeedSquared - sqrtDiscriminant) / (Grenade_Gravity * distanceXZ);
                angle                   = Math.Atan(tanAngle);
            }
            else
            {
                // Unreachable: Aim for "furthest possible" ballistic throw.
                // On level ground that's 45°. For large height differences, nudge slightly.
                angle = MathHelper.PiOver4; // 45°.

                // Tiny bias: If target is far above/below, tilt a bit up/down (clamped ±10°).
                if (Math.Abs(deltaY) > 0.5f)
                {
                    double slope = Math.Atan2(deltaY, Math.Max(distanceXZ, 1e-3f)); // -π/2..π/2.
                    angle += MathHelper.Clamp(
                                (float)(0.20 * slope),                              // Gentle scale.
                                -MathHelper.ToRadians(10f),
                                 MathHelper.ToRadians(10f));
                }
            }

            // Build final forward vector: cos(angle)*XZ + sin(angle)*Up.
            float   cosAngle = (float)Math.Cos(angle);
            float   sinAngle = (float)Math.Sin(angle);
            Vector3 forward  = Vector3.Normalize(directionXZ * cosAngle + Vector3.Up * sinAngle);

            // Stable 'up' (avoid near-parallel with forward).
            Vector3 up = Vector3.Up;
            if (Math.Abs(Vector3.Dot(forward, up)) > 0.999f) up = Vector3.Right;

            localToWorld = Matrix.CreateWorld(eyePosition, forward, up);
            return true;
        }
        #endregion

        #region Helpers

        /// <summary>
        /// Flat (no-drop) aim using your existing AimMath helper.
        /// </summary>
        private static bool AimFlat(PerspectiveCamera cam, Entity target, Vector3 offset, out Matrix l2w)
        {
            // lead=0, roll=0 -> straight look-at.
            return AimMath.TryMakeAimL2W(cam, target, offset, 0f, Angle.Zero, out l2w);
        }

        /// <summary>
        /// True if the engine will add the +0.015 rad upward "drop kick" at send time.
        /// We probe via reflection to keep this code robust across builds.
        /// </summary>
        private static bool NeedsDropCompensation(GunInventoryItemClass gun)
        {
            var pi = AccessTools.Property(gun.GetType(), "NeedsDropCompensation");
            if (pi != null && pi.PropertyType == typeof(bool))
            {
                try { return (bool)pi.GetValue(gun, null); } catch { /* fall through */ }
            }
            return false; // Safest fallback if the property is absent/inaccessible.
        }
        #endregion
    }
    #endregion

    #region AimbotRateLimiter

    /// <summary>
    /// CentraliMob rate limiter for aimbot-triggered shots.
    /// Behavior:
    /// - RapidFire ON & no explicit delay -> Do not throttle here (RF logic handles cadence elsewhere).
    /// - Explicit delay provided (ms)     -> Throttle by wall-clock and reset base cooldown to keep HUD feel.
    /// - No explicit delay                -> Respect the game's own OneShotTimer; if the timer is near-zero/absent,
    ///                                       Apply a per-weapon minimum (e.g., bolt rifle) for aimbot only.
    /// </summary>
    internal static class AimbotRateLimiter
    {
        #region State

        // Per-weapon (by Item ID) next-allowed timestamp.
        private static readonly Dictionary<InventoryItemIDs, DateTime> _next =
            new Dictionary<InventoryItemIDs, DateTime>();

        // Optional per-weapon minimum aimbot delay (ms) when vanilla timer is effectively zero.
        private static readonly Dictionary<InventoryItemIDs, int>      _minAimbotDelayMs =
            new Dictionary<InventoryItemIDs, int>();

        #endregion

        #region Configuration

        /// <summary>Register/override a minimum aimbot delay for a specific item ID (milliseconds).</summary>
        public static void ConfigureMinDelay(InventoryItemIDs id, int ms)
            => _minAimbotDelayMs[id] = Math.Max(0, ms);

        #endregion

        #region Helpers

        #region RNG: Buffered CSPRNG (Zero Bias)

        /// <summary>
        /// In-Game Benchmarks (.NET 4.7.2):
        /// --------------------------------------------------
        /// Original (new RNGCSP per call, modulo bias)
        /// Time:    909.52 ms   Throughput: 2,198.95 ops/ms    Uniformity≈ 0.0044   Checksum: 62989811
        /// --------------------------------------------------
        /// GetInt32-style (CSPRNG, zero bias)
        /// Time:    796.34 ms   Throughput: 2,511.48 ops/ms    Uniformity≈ 0.0056   Checksum: 63062655
        /// --------------------------------------------------
        /// Buffered CSPRNG (zero bias)
        /// Time:     45.75 ms   Throughput: 43,711.36 ops/ms   Uniformity≈ 0.0041   Checksum: 62981794
        /// --------------------------------------------------
        /// Fast PRNG (PCG-ish, seeded from CSPRNG)
        /// Time:     50.46 ms   Throughput: 39,635.67 ops/ms   Uniformity≈ 0.0030   Checksum: 62995908
        /// --------------------------------------------------
        /// </summary>

        /// <summary>
        /// Fast, thread-local buffered CSPRNG with rejection sampling to avoid modulo bias.
        /// Perfect for "random delay" jitter without locks or allocations on the hot path.
        /// </summary>
        internal static class AimbotDelayRng
        {
            // One CSPRNG for the process; used only on buffer refill (rare).
            private static readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();

            // Per-thread 4KB buffer (~1024 draws per refill).
            [ThreadStatic] private static byte[] _buf;
            [ThreadStatic] private static int    _ofs;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static uint NextU32()
            {
                var b = _buf;

                // Need 4 bytes available for this read.
                if (b == null || _ofs > (b.Length - 4))
                {
                    b = _buf ?? (_buf = new byte[4096]);
                    _rng.GetBytes(b);
                    _ofs = 0;
                }

                uint v = (uint)(b[_ofs]
                             | (b[_ofs + 1] << 8)
                             | (b[_ofs + 2] << 16)
                             | (b[_ofs + 3] << 24));
                _ofs += 4;
                return v;
            }

            /// <summary>Uniform [0, n) with zero modulo bias.</summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static int NextInt(int n)
            {
                if (n <= 0) throw new ArgumentOutOfRangeException(nameof(n));
                uint un = (uint)n;
                uint limit = (uint.MaxValue / un) * un; // Largest multiple < 2^32.
                uint r;
                do { r = NextU32(); } while (r >= limit);
                return (int)(r % un);
            }

            /// <summary>
            /// Uniform delay in milliseconds: [0..maxMs] inclusive. maxMs <= 0 => 0.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static int NextDelayMs(int maxMs)
            {
                if (maxMs <= 0) return 0;

                // Avoid overflow paranoia; your max is clamped anyway (e.g. 60,000).
                return NextInt(maxMs + 1);
            }
        }
        #endregion

        /// <summary>
        /// Resolve a per-weapon minimum delay for aimbot fire when the base cooldown is missing/near-zero.
        /// Priority: Explicit ID mapping -> type check -> no minimum.
        /// </summary>
        private static int? GetMinDelayFor(InventoryItem item)
        {
            if (item == null) return null;

            // 1) Explicit per-ID mapping wins.
            var id = item.ItemClass.ID;
            if (_minAimbotDelayMs.TryGetValue(id, out var ms))
                return ms;

            // 2) Simple type-based fallback.
            if (item.ItemClass is BoltRifleInventoryItemClass)
                return 100; // ~10 shots/sec.

            // 3) No custom delay.
            return null;
        }
        #endregion

        #region Public API

        #region Random Delay (Aimbot Jitter)

        private static readonly Dictionary<InventoryItemIDs, DateTime> _nextRandom =
            new Dictionary<InventoryItemIDs, DateTime>();

        /// <summary>
        /// Slider value is stored as float. Treat it as "max ms" (0 disables).
        /// Clamp to something sane to avoid accidental huge values.
        /// </summary>
        private static int GetRandomDelayMaxMs()
        {
            float v = CastleWallsMk2._aimbotRandomDelay;
            if (v <= 0f) return 0;

            int ms = (int)Math.Round(v);
            if (ms < 0) ms = 0;
            if (ms > 60000) ms = 60000; // Clamp to 60s.
            return ms;
        }
        #endregion

        /// <summary>
        /// Consume a "shot token" for the current item at a given cadence.
        /// Returns true if a shot is allowed right now (and updates internal state).
        /// </summary>
        public static bool TryConsume(InventoryItem item, int? delayMs)
        {
            if (item == null) return false;

            // Rapid-fire ON and no explicit override -> allow (RF system shrinks timers elsewhere).
            if (WeaponExtensions._rapidFireOn && delayMs == null)
                return true;

            // Caller-provided override (milliseconds): Gate by wall clock and reset vanilla cooldown
            // so HUD animations/sounds remain consistent with a "shot happened".
            if (delayMs != null)
            {
                var id  = item.ItemClass.ID;
                var now = DateTime.UtcNow;

                if (_next.TryGetValue(id, out var allowAt) && now < allowAt)
                    return false;

                _next[id] = now.AddMilliseconds(Math.Max(0, delayMs.Value));
                CooldownBridge.Reset(CooldownBridge.TryGetBaseTimer(item));
                return true;
            }

            // Default: Respect the game's base cooldown (vanilla ROF).
            var t = CooldownBridge.TryGetBaseTimer(item);

            // If the base cooldown is effectively zero/missing (e.g., bolt rifle),
            // apply a minimal per-weapon cadence for aimbot ONLY (manual fire remains vanilla).
            var minMs = GetMinDelayFor(item);
            if (minMs != null)
            {
                var id  = item.ItemClass.ID;
                var now = DateTime.UtcNow;

                if (_next.TryGetValue(id, out var allowAt) && now < allowAt)
                    return false;

                _next[id] = now.AddMilliseconds(minMs.Value);
                CooldownBridge.Reset(t); // Keep effects/animations in sync.
                return true;
            }

            // No mapping and no real base timer -> allow, but still reset so HUD/FX stay consistent.
            CooldownBridge.Reset(t);
            return true;
        }

        /// <summary>
        /// Extra per-shot random gate for vanilla cadence mode.
        /// Roll happens only when we "consume" (so we don't reroll every tick while waiting).
        /// </summary>
        public static bool TryConsumeRandomDelayOnly(InventoryItem item)
        {
            if (item == null) return false;

            int maxMs = GetRandomDelayMaxMs();
            if (maxMs <= 0) return true; // Disabled.

            var id = item.ItemClass.ID;
            var now = DateTime.UtcNow;

            if (_nextRandom.TryGetValue(id, out var allowAt) && now < allowAt)
                return false;

            int extra = AimbotDelayRng.NextDelayMs(maxMs);
            _nextRandom[id] = now.AddMilliseconds(extra);
            return true;
        }

        /// <summary>
        /// Custom cadence wrapper: schedules (baseDelay + randomExtra).
        /// Random is rolled only when the base limiter allows a consume.
        /// </summary>
        public static bool TryConsumeWithRandom(InventoryItem item, int baseDelayMs)
        {
            if (item == null) return false;

            int baseMs = Math.Max(0, baseDelayMs);
            int maxMs  = GetRandomDelayMaxMs();

            var id  = item.ItemClass.ID;
            var now = DateTime.UtcNow;

            if (_next.TryGetValue(id, out var allowAt) && now < allowAt)
                return false;

            int extra = AimbotDelayRng.NextDelayMs(maxMs);
            _next[id] = now.AddMilliseconds(baseMs + extra);

            // Keep existing "HUD feel" behavior consistent with the current limiter design.
            CooldownBridge.Reset(CooldownBridge.TryGetBaseTimer(item));
            return true;
        }
        #endregion
    }
    #endregion

    #region AimbotFire

    internal static class AimbotFire
    {
        #region FieldRefs (Entity internals)

        // Harmony FieldRefs into Entity internals so we can temporarily force camera transform.
        private static readonly AccessTools.FieldRef<Entity, Matrix> _ltwRef      =
            AccessTools.FieldRefAccess<Entity, Matrix>("_localToWorld");
        private static readonly AccessTools.FieldRef<Entity, bool>   _ltwDirtyRef =
            AccessTools.FieldRefAccess<Entity, bool>("_ltwDirty");
        #endregion

        #region RAII Helpers (Camera / ADS)

        /// <summary>
        /// RAII-style helper: Temporarily replace the camera's LocalToWorld for a single shot,
        /// then restore it even if an exception occurs.
        /// </summary>
        private readonly struct TempCamL2W : IDisposable
        {
            private readonly PerspectiveCamera _cam;
            private readonly Matrix _orig;
            private readonly bool _origDirty;

            public TempCamL2W(PerspectiveCamera cam, Matrix newL2W)
            {
                _cam              = cam;
                _orig             = _ltwRef(cam);
                _origDirty        = _ltwDirtyRef(cam);

                _ltwRef(cam)      = newL2W;
                _ltwDirtyRef(cam) = false; // Prevent recompute for this frame.
            }

            public void Dispose()
            {
                _ltwRef(_cam)      = _orig;
                _ltwDirtyRef(_cam) = _origDirty;
            }
        }

        /// <summary>
        /// RAII-style helper: One-shot ADS accuracy override (swaps hipfire spread with ADS
        /// spread for the duration).
        /// </summary>
        private readonly struct TempADSAccuracy : IDisposable
        {
            private readonly GunInventoryItemClass _cls;
            private readonly Angle _origMin, _origMax;
            private readonly bool _active;

            public TempADSAccuracy(GunInventoryItemClass cls, bool enable)
            {
                _cls    = cls;
                _active = enable && cls != null;

                if (_active)
                {
                    // Save hipfire spread.
                    _origMin = cls.MinInnaccuracy;
                    _origMax = cls.MaxInnaccuracy;

                    // Replace with shouldered (ADS) spread.
                    cls.MinInnaccuracy = cls.ShoulderedMinAccuracy;
                    cls.MaxInnaccuracy = cls.ShoulderedMaxAccuracy;
                }
                else
                {
                    // Dummy values (won't be used when !_active).
                    _origMin = default;
                    _origMax = default;
                }
            }

            public void Dispose()
            {
                // Put the original hip-fire spread back, even if the shot failed.
                if (_active)
                {
                    _cls.MinInnaccuracy = _origMin;
                    _cls.MaxInnaccuracy = _origMax;
                }
            }
        }
        #endregion

        #region Public API

        /// <summary>
        /// Fires the current gun via vanilla HUD.Shoot() while temporarily substituting
        /// the camera's LocalToWorld with <paramref name="fireL2W"/>. This preserves
        /// all built-in behavior (ammo, spread, recoil, sounds, messages).
        /// </summary>
        #pragma warning disable IDE0019 // Use pattern matching.
        public static bool TryFireWithAimMatrix(Matrix fireL2W, bool infiniteAmmo = false)
        {
            var hud  = InGameHUD.Instance;
            var game = CastleMinerZGame.Instance;
            var inv  = game?.LocalPlayer?.PlayerInventory;
            var cam  = game?.LocalPlayer?.FPSCamera as PerspectiveCamera;
            if (hud == null || inv == null || cam == null) return false;

            // Only proceed if a gun-like item is active (HUD.Shoot expects a gun class).
            if (!(inv.ActiveInventoryItem is GunInventoryItem gi)) return false;

            // Route aimbot shots through the limiter.
            if (!AimbotRateLimiter.TryConsume(inv.ActiveInventoryItem, delayMs: null))
                return false;

            // Decide if to use ADS spread for THIS shot.
            bool wantADS =
                AimbotController.IsShouldered          // Use only when "aiming".
                && !WeaponExtensions._superGunStatsOn; // If perfect accuracy is already forced, skip.

            using (new TempCamL2W(cam, fireL2W))
            using (new TempADSAccuracy(gi.GunClass, wantADS))
            {
                // Route through the game's normal shooting pipeline.
                if (infiniteAmmo || gi.RoundsInClip > 0)
                    hud.Shoot(gi.GunClass);

                // Consume ammo if infinite ammo is not enabled.
                if (!infiniteAmmo && gi.RoundsInClip > 0) gi.RoundsInClip--;
            }
            return true;
        }
        #pragma warning restore IDE0019
        #endregion
    }
    #endregion

    #region AimbotGrenades

    /// <summary>
    /// Handles grenade throws for the aimbot:
    /// - Rapid-fire ON  -> Bypasses the vanilla animation and sends GrenadeMessage at a fixed cadence.
    /// - Rapid-fire OFF -> Kicks the normal cook->throw animation, but injects a one-shot aim matrix so the throw is accurate.
    /// </summary>
    internal static class AimbotGrenades
    {
        #region State / Config

        // One-shot orientation used ONLY when we let the vanilla animation perform the send.
        // A Harmony prefix on GrenadeMessage.Send will consume this once, then clear it.
        private static Matrix?  _pendingAimL2W;

        #endregion

        #region Public API

        /// <summary>
        /// Aim and throw a grenade at <paramref name="target"/>.
        /// - Builds an aim orientation from the current camera to the target (+offset, optional ballistic drop compensation).
        /// - RF ON  -> Directly sends a GrenadeMessage at a fixed cadence (skips local animation; preserves vanilla 5s fuse).
        /// - RF OFF -> Starts the normal cook -> throw animation and supplies a one-shot aim override.
        /// </summary>
        /// <remarks>
        /// Safety:
        /// - If aim math fails, falls back to the camera's current LocalToWorld orientation.
        /// - Only acts when the active item is a GrenadeInventoryItemClass.
        /// </remarks>
        #pragma warning disable IDE0019 // Use pattern matching (kept broader for older compilers).
        public static void TryThrowAtTarget(Entity target, Vector3 offset, bool adjustForBulletDrop = false)
        {
            #region Guards & Inputs

            var game = CastleMinerZGame.Instance;
            var cam  = game?.LocalPlayer?.FPSCamera as PerspectiveCamera;
            var hud  = InGameHUD.Instance;
            if (cam == null || hud == null)
                return;

            // Must actually be holding a grenade item class right now.
            var inv    = game.LocalPlayer?.PlayerInventory;
            var item   = inv?.ActiveInventoryItem;
            var gClass = hud.ActiveInventoryItem?.ItemClass as GrenadeInventoryItemClass;
            if (gClass == null)
                return;
            #endregion

            #region Aim Selection (Ballistic vs Flat)

            // Choose an orientation to throw with:
            // - When adjustForBulletDrop = true, solve a low-arc grenade trajectory.
            // - Otherwise, flat/straight aim using the AimMath helper.
            Matrix orient;
            if (adjustForBulletDrop)
            {
                // Try ballistic grenade aim; If it fails, fall back to normal aimbot aim.
                if (!AimbotBallistics.TryMakeAimL2W_Grenade(cam, target, offset, out orient))
                {
                    // Normal aimbot throw matrix (flat aim). If that somehow fails, then use camera forward.
                    if (!AimMath.TryMakeAimL2W(cam, target, offset, 0f, Angle.Zero, out orient))
                        orient = cam.LocalToWorld;
                }
            }
            else
            {
                // No drop correction -> Always use normal aimbot aim.
                if (!AimMath.TryMakeAimL2W(cam, target, offset, 0f, Angle.Zero, out orient))
                    orient = cam.LocalToWorld;
            }
            #endregion

            #region Rapid-Fire Path (Direct Network Send; Skips Local Animation)

            if (WeaponExtensions._rapidFireOn)
            {
                // Use vanilla full fuse (5 seconds).
                GrenadeMessage.Send(
                    (LocalNetworkGamer)game.LocalPlayer.Gamer,
                    orient,
                    gClass.GrenadeType,
                    5f
                );

                // Prevent vanilla animation from also sending a throw this tick.
                var p = game.LocalPlayer;
                p.PlayGrenadeAnim     = false;
                p.ReadyToThrowGrenade = false;
                p.Avatar?.Animations?.ClearAnimation(3, TimeSpan.Zero);
                return;
            }
            #endregion

            #region Vanilla Animation Path (RF OFF; Aim Once, Then Let Game Send)

            // Derive vanilla cadence and let the shared limiter enforce it.
            int delayMs = (int)Math.Max(1, gClass.CoolDownTime.TotalMilliseconds + 750 /* 750ms: Account for pin-pulling animation. */);
            if (!AimbotRateLimiter.TryConsumeWithRandom(item, delayMs))
                return; // Not time yet.

            // Store one-shot orientation for your GrenadeMessage.Send prefix to consume,
            // then kick the animation state machine to perform the throw.
            _pendingAimL2W = orient;

            var ply = game.LocalPlayer;
            ply.PlayGrenadeAnim     = true; // Start cook.
            ply.ReadyToThrowGrenade = true; // Allow throw.
            #endregion
        }
        #pragma warning restore IDE0019
        #endregion

        #region Hook Helpers (Consumed By Harmony Prefix On GrenadeMessage.Send)

        /// <summary>
        /// Returns the pending aim matrix (if any) for the vanilla-send path and clears it (one-shot).
        /// </summary>
        public static bool TryConsumePending(out Matrix l2w)
        {
            if (_pendingAimL2W.HasValue)
            {
                l2w = _pendingAimL2W.Value;
                _pendingAimL2W = null; // One-shot: Consume & clear.
                return true;
            }
            l2w = default;
            return false;
        }
        #endregion
    }
    #endregion
}