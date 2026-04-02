/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

#pragma warning disable IDE0060       // Silence IDE0060.
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using DNA.Net.GamerServices;
using System.Reflection;
using System.Linq;
using HarmonyLib;                     // Harmony patching library.
using DNA.Input;
using DNA.Net;
using System;
using DNA;

using static ModLoader.LogSystem;     // For Log(...).

namespace VoiceChat
{
    /// <summary>
    /// All Harmony patches in one place. Using ApplyAllPatches()
    /// will scan this assembly for nested [HarmonyPatch] classes
    /// and apply them, then log exactly what got patched.
    /// </summary>
    class GamePatches
    {
        #region Patcher Initiation

        // Keep a handle to this Harmony instance so we can unpatch later.
        private static Harmony _harmony;
        private static string  _harmonyId;

        /// <summary>
        /// Best-effort Harmony bootstrap:
        /// - Scans this assembly for all classes marked with [HarmonyPatch].
        /// - All classes marked with the additional [HarmonySilent] attribute will have logging silenced.
        /// - Patches each class independently inside a try/catch (one bad target won't kill the rest).
        /// - Logs a per-class result and a final summary of methods actually patched by our Harmony ID.
        /// - Leaves your UI wiring call in place after patching.
        /// </summary>
        public static void ApplyAllPatches()
        {
            Log("[Harmony] Starting game patching.");

            // Create a stable, unique Harmony ID for this mod. Using the namespace helps avoid collisions.
            _harmonyId = $"castleminerz.mods.{typeof(GamePatches).Namespace}.patches"; // Unique ID based on namespace.
            _harmony   = new Harmony(_harmonyId);                                      // Create & store the Harmony instance.

            // Choose which assembly to scan for patch classes.
            // If you split patches across multiple assemblies, call this routine for each assembly.
            Assembly asm = typeof(GamePatches).Assembly;

            int successCount = 0;
            int failCount    = 0;

            // Enumerate every class that has at least one [HarmonyPatch] attribute,
            // and patch it independently (best-effort).
            foreach (var patchType in EnumeratePatchTypes(asm))
            {
                try
                {
                    // Create a processor for this patch class and apply all of its prefixes/postfixes/transpilers.
                    var proc    = _harmony.CreateClassProcessor(patchType);
                    var targets = proc?.Patch(); // List<MethodBase> of target methods Harmony hooked (may be null).
                    successCount++;

                    /*
                    // NOTE: Don't show silent patch containers.
                    if (!IsSilent(patchType))
                    {
                        int targetCount = targets?.Count ?? 0;
                        Log($"[Harmony] Patched {patchType.FullName} ({targetCount} target(s)).");
                    }
                    */

                    int targetCount = targets?.Count ?? 0;
                    Log($"[Harmony] Patched {patchType.FullName} ({targetCount} target(s)).");
                }
                catch (Exception ex)
                {
                    failCount++;
                    Log($"[Harmony] FAILED patching {patchType.FullName}: {ex.GetType().Name}: {ex.Message}.");
                }
            }

            // Summarize what we actually patched (filter by Owner == our Harmony ID).
            var ours = _harmony.GetPatchedMethods()
                               .Where(m =>
                               {
                                   var info = Harmony.GetPatchInfo(m);
                                   return info != null && (info.Owners?.Contains(_harmonyId) ?? false);
                               })
                               .ToList();

            // Print per-method details, but filter out any silent patches FIRST.
            foreach (var m in ours)
            {
                var info = Harmony.GetPatchInfo(m);
                if (info == null) continue;

                // Filter out silent patches before printing anything.
                var prefixes    = Filter(info.Prefixes).ToList();
                var postfixes   = Filter(info.Postfixes).ToList();
                var transpilers = Filter(info.Transpilers).ToList();

                // If nothing remains (all were silent), don't log this method at all.
                if (prefixes.Count == 0 && postfixes.Count == 0 && transpilers.Count == 0) continue;

                // Show filtered counts (not the raw/total counts).
                Log($"[Harmony] Patched method: {Describe(m)} | " +
                    $"[Prefixes={prefixes.Count}] [Postfixes={postfixes.Count}] [Transpilers={transpilers.Count}].");

                foreach (var p in prefixes)    Log($"  • Prefix    : {Describe(p.PatchMethod)}.");
                foreach (var p in postfixes)   Log($"  • Postfix   : {Describe(p.PatchMethod)}.");
                foreach (var p in transpilers) Log($"  • Transpiler: {Describe(p.PatchMethod)}.");
            }

            Log($"[Harmony] Patching complete. Success={successCount}, Failed={failCount}, MethodsPatchedByUs={ours.Count}.");
        }

        /// <summary>
        /// Unpatch everything applied by this mod's Harmony ID only
        /// (restores original game methods without touching other mods).
        /// </summary>
        public static void DisableAll()
        {
            if (_harmony != null)
            {
                Log($"[Harmony] Unpatching all ({_harmonyId}).");
                _harmony.UnpatchAll(_harmonyId);
            }
        }

        #region Silent Attribute

        /// <summary>
        /// Lets you tag a whole patch class or a single method so the patch-reporting logger will ignore it.
        /// </summary>
        [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
        internal sealed class HarmonySilentAttribute : Attribute { };

        #endregion

        #region Patcher Helpers

        /// <summary>
        /// Return true if the method or its declaring type is marked with [HarmonySilent].
        /// </summary>
        static bool IsSilent(MemberInfo mi)
        {
            if (mi == null) return false;

            // Respect [HarmonySilent] on the member itself.
            if (mi.IsDefined(typeof(HarmonySilentAttribute), inherit: false))
                return true;

            // Respect [HarmonySilent] on declaring type.
            var dt = (mi as MethodBase)?.DeclaringType ?? mi as Type;
            if (dt != null && dt.IsDefined(typeof(HarmonySilentAttribute), inherit: false))
                return true;

            return false;
        }

        /// <summary>
        /// Filters out patches whose patch method (or its declaring type) is marked "silent".
        /// </summary>
        static IEnumerable<Patch> Filter(IEnumerable<Patch> src)
            => (src ?? Enumerable.Empty<Patch>()).Where(p => !IsSilent(p.PatchMethod));

        /// <summary>
        /// Finds all types that are Harmony patch containers in the given assembly
        /// (i.e., classes marked with [HarmonyPatch]). Using an attribute scan keeps us
        /// from trying to patch non-patch helper classes accidentally.
        /// </summary>
        private static IEnumerable<Type> EnumeratePatchTypes(Assembly asm)
        {
            // AccessTools.GetTypesFromAssembly is defensive (skips type-load failures).
            foreach (var t in AccessTools.GetTypesFromAssembly(asm))
            {
                if (t == null || !t.IsClass) continue;

                // Harmony 2.x attribute name is "HarmonyLib.HarmonyPatch".
                // Compare by FullName or simple Name to stay robust across versions/builds.
                bool hasPatchAttr = t.GetCustomAttributes(inherit: true)
                                    .Any(a => a != null &&
                                              (a.GetType().FullName == "HarmonyLib.HarmonyPatch" ||
                                               a.GetType().Name     == "HarmonyPatch"));
                if (hasPatchAttr)
                    yield return t;
            }
        }

        /// <summary>
        /// Nice method formatter for log output: TypeName.MethodName(T0, T1, ...).
        /// </summary>
        private static string Describe(MethodBase m)
        {
            if (m == null) return "(null)";
            try
            {
                string type = m.DeclaringType != null ? m.DeclaringType.FullName : "(global)";
                string pars = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name));
                return $"{type}.{m.Name}({pars})";
            }
            catch
            {
                // Fallback if reflection blows up for any reason.
                return m.ToString();
            }
        }
        #endregion

        #endregion

        #region Patches

        // ==========================================================================================================
        // Voice Chat - Patch Set (Fragmented capture, PTT, self-mute, ensure-start, HUD, teardown).
        //
        // CONFIG KEYS (read via VoiceChatConfigStore.Current):
        //    PttKey            : Keys (default V).
        //    FragmentSize      : int (bytes) - 600..4000 recommended (default 2000).
        //    EnsureStart       : bool - start voice once session exists (default true).
        //    MuteSelf          : bool - do not play our own voice locally (default true).
        //    MicBufferMs       : int? - set microphone BufferDuration in ms (null = leave device default) (removed).
        //    ShowSpeakerHud    : bool - toggle HUD banner (default true).
        //    HudAnchor         : string - TopLeft/TopRight/BottomLeft/BottomRight or TL/TR/BL/BR.
        //    HudSeconds        : double - duration to show banner after last packet.
        //    HudFadeSeconds    : double - fade-out tail.
        //
        // BIGGEST ORIGINAL ISSUES WE FIX:
        //  1) Mic handler indexed the A-Law map with a SIGNED sample -> negative index -> crash.
        //  2) Sending full 100 ms frames (~4.4-4.8 KB A-law) overloaded receive buffers -> "Data buffer too small".
        //  3) One giant packet per buffer caused queue stalls/freeze when combined with in-tick dispatch.
        //  4) ProcessMessage always submitted the FULL preallocated play buffer -> stale tail clicks & pops.
        //  5) No PTT; mic ran continuously once started.
        //  6) Loop played our own voice (sidetone) -> confusing echo.
        //  7) VoiceChat sometimes never constructed (timing) -> one client hears only themselves.
        //  8) No graceful teardown -> dangling events, device still started after leave/end.
        //
        // HOW WE OVERCAME THEM:
        //  • Encode fix: Assemble 16-bit PCM LE and index A-Law with (ushort) sample.
        //  • Fragmentation: Split each 100 ms capture into safe slices (configurable FragmentSize).
        //  • Exact submit: Decode only the bytes we received; submit exact length to playback.
        //  • PTT hook: Start/stop mic on configured key (default V).
        //  • Self-mute: Drop self-originating voice packets in ProcessMessage.
        //  • Ensure-start: One-time start of VoiceChat once session/local gamer exists.
        //  • Teardown: Stop mic, stop/dispose playback, clear reference on leave/end.
        //
        // LIMITATIONS (and mitigations):
        //  • Fragmentation increases per-tick sends (CPU/network). Keep FragmentSize as large as peers allow.
        //  • If device BufferDuration is clamped at 100 ms, you still send multiple fragments per tick.
        //  • If many speakers talk at once, you may build up a playback queue; DynamicSoundEffectInstance
        //    handles it, but you can lower FragmentSize or BufferDuration to reduce latency/pressure.
        // ==========================================================================================================

        #region [Patch 1] VoiceChat._microphone_BufferReady -> Fragmented, Safe Encode

        /*
         SUMMARY
           • Replaces private mic handler to fix signed-index bug and to fragment the outgoing A-law
             payload into multiple VoiceChatMessage packets this tick, each ≤ FragmentSize bytes.
           • Reads config: FragmentSize (clamped 600..4000).

         BEFORE (conceptual):
             mic.GetData(_micBuffer);
             for (i = 0; i < _micBuffer.Length/2; i++) {
                 short sample = (short)((_micBuffer[rp+1] << 8) | _micBuffer[rp]); // (often OK).
                 _sendBuffer[i] = _pcmToALawMap[(int)sample];                      // BUG: negative index when sample < 0.
                 rp += 2;
             }
             VoiceChatMessage.Send(gamer, _sendBuffer);                            // BIG: ~4.4 KB payload -> Frequent receive overflow.

         AFTER (patched):
             mic.GetData(_micBuffer);
             for (i = 0; i < samples; i++) {
                 short s = (short)(_micBuffer[rp] | (_micBuffer[rp+1] << 8));      // Little-endian.
                 encoded[i] = _pcmToALawMap[(ushort)s];                            // FIX: UNSIGNED index 0..65535.
                 rp += 2;
             }
             for (off = 0; off < encodedLen; off += FRAG)                          // FRAG from config.
                 VoiceChatMessage.Send(slice);                                     // Multiple small messages, no overflow.
        */

        [HarmonyPatch(typeof(DNA.Net.VoiceChat), "_microphone_BufferReady")]
        internal static class VoiceMicHandler_Frag
        {
            static bool Prefix(object __instance)
            {
                try
                {
                    var t         = __instance.GetType();
                    var mic       = (Microphone)AccessTools.Field(t, "_microphone").GetValue(__instance);
                    var micBuffer = (byte[])AccessTools.Field(t, "_micBuffer").GetValue(__instance);
                    var gamer     = (LocalNetworkGamer)AccessTools.Field(t, "_gamer").GetValue(__instance);
                    var map       = (byte[])AccessTools.Field(t, "_pcmToALawMap").GetValue(null);
                    if (mic == null || micBuffer == null || gamer == null || map == null) return false;

                    // Pull one device buffer (often 100ms).
                    mic.GetData(micBuffer);

                    // Encode PCM(16-bit, LE) -> A-law(8-bit).
                    int samples    = micBuffer.Length >> 1;
                    int encodedLen = samples;
                    var encoded    = new byte[encodedLen];

                    int rp = 0;
                    for (int i = 0; i < encodedLen; i++)
                    {
                        short s = (short)(micBuffer[rp] | (micBuffer[rp + 1] << 8)); // LE.
                        encoded[i] = map[(ushort)s];                                 // FIX: Unsigned index.
                        rp += 2;
                    }

                    // Broadcast as multiple packets this frame (fragmented).
                    if (gamer.Session != null && gamer.Session.RemoteGamers.Count > 0)
                    {
                        int FRAG = Math.Max(600, Math.Min(4000, VoiceChatConfigStore.Current.FragmentSize));
                        for (int off = 0; off < encodedLen; off += FRAG)
                        {
                            int n = Math.Min(FRAG, encodedLen - off);
                            var slice = new byte[n];
                            Buffer.BlockCopy(encoded, off, slice, 0, n);
                            VoiceChatMessage.Send(gamer, slice);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Voice][Frag] {ex}.");
                }
                return false; // Skip original buggy handler.
            }
        }
        #endregion

        #region [Patch 2] Ensure VoiceChat Starts Once Session/Local Gamer Exists

        /*
         SUMMARY
           • Some clients never constructed _voiceChat due to join timing; they would hear only themselves.
           • This postfix runs every Update and, if _voiceChat is null but a local gamer exists, calls
             DNAGame.StartVoiceChat(LocalNetworkGamer). Controlled by EnsureStart (config).

         BEFORE (conceptual):
             // No deterministic start; relies on specific join event paths.

         AFTER:
             if (EnsureStart && _voiceChat == null && Session.LocalGamers.Count > 0)
                 StartVoiceChat(LocalGamers[0]);
        */

        [HarmonyPatch]
        internal static class VoiceEnsureStart
        {
            static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(DNAGame), "Update", new[] { typeof(GameTime) });

            static void Postfix(DNAGame __instance)
            {
                try
                {
                    if (!VoiceChatConfigStore.Current.EnsureStart) return;

                    var vcField = AccessTools.Field(typeof(DNAGame), "_voiceChat");
                    if (vcField.GetValue(__instance) != null) return;

                    // Pull from session - local gamer does not unhook once exposed.

                    #pragma warning disable IDE0019 // Silance 'Use pattern matching'.
                    var nsField = AccessTools.Field(typeof(DNAGame), "_networkSession");
                    var session = nsField?.GetValue(__instance) as NetworkSession;
                    if (session == null || session.LocalGamers.Count == 0) return;
                    var local   = session.LocalGamers[0] as LocalNetworkGamer;
                    #pragma warning restore IDE0019

                    if (local == null) return;

                    var start = AccessTools.Method(typeof(DNAGame), "StartVoiceChat", new[] { typeof(LocalNetworkGamer) });
                    start?.Invoke(__instance, new object[] { local });

                    Log($"[Voice.Start] Started voice chat: ptt={VoiceChatConfigStore.Current.PttKey}, frag={VoiceChatConfigStore.Current.FragmentSize}, muteSelf={VoiceChatConfigStore.Current.MuteSelf}, hud={VoiceChatConfigStore.Current.ShowSpeakerHud}/{VoiceChatConfigStore.Current.HudAnchor}.");
                }
                catch { /* Keep Update safe. */ }
            }

            static void Log(string s) => ModLoader.LogSystem.Log(s);
        }
        #endregion

        #region [Patch 3] ProcessMessage -> Self-Mute + Exact-Length Submit + HUD

        /*
         SUMMARY
           • Mutes our own voice packets (sidetone) if MuteSelf = true.
           • Decodes only the bytes we received and submits EXACT length (pairs with fragmentation).
           • Announces who spoke to the HUD (if enabled).

         BEFORE (from decompiled):
             for (i = 0; i < message.AudioBuffer.Length; i++) {
                 short sample = _aLawToPcmMap[ message.AudioBuffer[i] ];
                 _playBuffer[write++] = (byte)(sample & 255);
                 _playBuffer[write++] = (byte)(sample >> 8);
             }
             _playbackEffect.SubmitBuffer(_playBuffer);  // NOTE: Submits FULL prealloc buffer, tail may be stale.

         AFTER:
             if (MuteSelf && message.Sender.IsLocal) return;
             decode -> outBuf (only length*2)
             _playbackEffect.SubmitBuffer(outBuf, 0, w); // Exact number of bytes decoded.
             SpeakerHUD.Heard(message.Sender);
        */

        [HarmonyPatch(typeof(DNA.Net.VoiceChat), "ProcessMessage")]
        internal static class Voice_ProcessMessage_FilterAndExact
        {
            static bool Prefix(DNA.Net.VoiceChat __instance, DNA.Net.VoiceChatMessage message)
            {
                // Mute self-echo (configurable).
                if (VoiceChatConfigStore.Current.MuteSelf && message?.Sender != null && message.Sender.IsLocal)
                    return false;

                try
                {
                    // (Optional HUD) record who spoke.
                    if (VoiceChatConfigStore.Current.ShowSpeakerHud)
                        SpeakerHUD.Heard(message.Sender);

                    var t   = typeof(DNA.Net.VoiceChat);
                    var sfx = (DynamicSoundEffectInstance)AccessTools.Field(t, "_playbackEffect").GetValue(__instance);
                    var map = (short[])AccessTools.Field(t, "_aLawToPcmMap").GetValue(null);
                    var dst = (byte[])AccessTools.Field(t, "_playBuffer").GetValue(__instance);

                    if (sfx == null || map == null || message?.AudioBuffer == null)
                        return false;

                    // Use preallocated _playBuffer if it fits; else allocate a one-off.
                    int needBytes = message.AudioBuffer.Length * 2;
                    byte[] outBuf = (dst != null && dst.Length >= needBytes) ? dst : new byte[needBytes];

                    int w = 0;
                    int n = message.AudioBuffer.Length;
                    for (int i = 0; i < n; i++)
                    {
                        short sample = map[message.AudioBuffer[i]];
                        outBuf[w++] = (byte)(sample & 0xFF); // Little-endian PCM.
                        outBuf[w++] = (byte)(sample >> 8);
                    }

                    if (w > 0)
                        sfx.SubmitBuffer(outBuf, 0, w);      // Submit EXACT decoded length.
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Voice][Process] {ex}.");
                }

                // We fully handled playback.
                return false;
            }
        }
        #endregion

        #region [Patch 4] Push-To-Talk (PTT) On Update (Configurable Key)

        /*
         SUMMARY
           • Starts mic when configured key is held; stops on release.
           • Reads config: PttKey. (Also safe-checks MicrophoneState to avoid redundant calls.)

         BEFORE:
             // No push-to-talk; mic runs once VoiceChat starts.

         AFTER:
             if (IsKeyDown(PttKey) && mic not started) mic.Start();
             if (!IsKeyDown(PttKey) && mic started)    mic.Stop();
        */

        [HarmonyPatch]
        internal static class PttUpdateHook
        {
            private static Keys PttKey => VoiceChatConfigStore.Current.PttKey;
            private static bool _held;

            static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(DNAGame), "Update", new[] { typeof(GameTime) });

            static void Postfix(DNAGame __instance)
            {
                try
                {
                    if (!(AccessTools.Field(typeof(DNAGame), "InputManager").GetValue(__instance) is InputManager input)) return;

                    var vc = AccessTools.Field(typeof(DNAGame), "_voiceChat").GetValue(__instance);
                    if (vc == null) return;

                    // OPTIONAL: Skip PTT while a chat/text screen is active.
                    if (DNA.CastleMinerZ.CastleMinerZGame.Instance?.GameScreen?.HUD?.IsChatting == true)
                        return;

                    // Ignore Ctrl+PTT combos (prevents paste/shortcuts from keying the mic).
                    var  ks      = input.Keyboard.CurrentState;
                    bool ctrl    = ks.IsKeyDown(Keys.LeftControl) || ks.IsKeyDown(Keys.RightControl);
                    bool alt     = ks.IsKeyDown(Keys.LeftAlt)     || ks.IsKeyDown(Keys.RightAlt);
                    bool win     = ks.IsKeyDown(Keys.LeftWindows) || ks.IsKeyDown(Keys.RightWindows);

                    bool keyDown = ks.IsKeyDown(PttKey);
                    bool pttDown = keyDown && !ctrl && !alt && !win;
                    if (!(AccessTools.Field(vc.GetType(), "_microphone").GetValue(vc) is Microphone mic)) return;

                    if (pttDown && !_held && mic.State != MicrophoneState.Started)
                    {
                        mic.Start();
                        _held = true;
                        // Log($"[Voice][PTT] {PttKey} down -> mic START.");
                    }
                    else if (!pttDown && _held && mic.State == MicrophoneState.Started)
                    {
                        mic.Stop();
                        _held = false;
                        // Log($"[Voice][PTT] {PttKey} up -> mic STOP.");
                    }
                }
                catch (Exception ex)
                {
                    Log($"[Voice][PTT] error: {ex.Message}.");
                }
            }

            private static void Log(string s) => ModLoader.LogSystem.Log(s);
        }
        #endregion

        #region [Patch 5] Teardown On Leave/End (Stop Mic, Stop/Dispose Playback)

        /*
         SUMMARY
           • Guarantees capture & playback stop when the local gamer leaves or session ends.
           • Avoids dangling device/event handlers across game state transitions.

         BEFORE:
             // Device may continue running; _voiceChat may persist after leave/end.

         AFTER:
             if (mic.Started) mic.Stop();
             sfx.Stop(); sfx.Dispose();
             _voiceChat = null;
        */

        [HarmonyPatch(typeof(DNAGame))]
        internal static class VoiceTearDown
        {
            static void StopAndNull(DNAGame game)
            {
                var vcField = AccessTools.Field(typeof(DNAGame), "_voiceChat");
                var vc = vcField?.GetValue(game);
                if (vc == null) return;

                try
                {
                    #pragma warning disable IDE0019 // Silance 'Use pattern matching'.
                    var tVC      = vc.GetType();
                    var mic      = AccessTools.Field(tVC, "_microphone")     ?.GetValue(vc) as Microphone;
                    var handler  = AccessTools.Field(tVC, "handler")         ?.GetValue(vc) as EventHandler<EventArgs>;
                    var sfx      = AccessTools.Field(tVC, "_playbackEffect") ?.GetValue(vc) as DynamicSoundEffectInstance;
                    #pragma warning restore IDE0019

                    // Unsubscribe first so BufferReady can't re-enter during stop/dispose.
                    if (mic != null && handler != null) { try { mic.BufferReady -= handler; } catch { } }

                    if (mic != null && mic.State == MicrophoneState.Started) { try { mic.Stop(); } catch { } }
                    try { sfx?.Stop();    } catch { }
                    try { sfx?.Dispose(); } catch { }
                }
                catch { /* Keep teardown resilient. */ }

                vcField?.SetValue(game, null);
                Log("[Voice.Stop] Teardown complete: Mic stopped, playback disposed, state cleared.");
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(DNAGame.LeaveGame), new Type[] { })]
            private static void Post_LeaveGame(DNAGame __instance) => StopAndNull(__instance);

            static void Log(string s) => ModLoader.LogSystem.Log(s);
        }
        #endregion

        #region [Patch 6] Speaker HUD Hooks (Update/Draw)

        /*
         SUMMARY
           • Ticks & draws a minimal "<Gamertag> is talking" banner, honoring HUD config.
           • Display is triggered inside ProcessMessage (remote only), and fades out.

         BEFORE:
             // No HUD.

         AFTER:
             SpeakerHUD.Tick(gameTime); // on Update.
             SpeakerHUD.Draw(game);     // on Draw.
        */

        [HarmonyPatch]
        internal static class SpeakerHUD_UpdatePatch
        {
            static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(DNAGame), "Update", new[] { typeof(GameTime) });

            static void Postfix(DNAGame __instance, GameTime gameTime)
                => SpeakerHUD.Tick(gameTime);
        }

        [HarmonyPatch]
        internal static class SpeakerHUD_DrawPatch
        {
            static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(DNAGame), "Draw", new[] { typeof(GameTime) });

            static void Postfix(DNAGame __instance)
                => SpeakerHUD.Draw(__instance);
        }
        #endregion

        #region [Patch 7] AudioEngine.SetGlobalVariable -> Skip Buggy "Reverb*" Globals (Optional)

        /*
         SUMMARY
           • Prevents noisy first-chance IndexOutOfRangeException from XNA/XACT when the game
             tries to set reverb globals that do not exist in the currently loaded .xgs.
           • Intercepts SetGlobalVariable for any name starting with "Reverb" and skips the call.
           • Logs once per unique missing global (value shown), then remains silent thereafter.

         BEFORE (conceptual):
             // Throws if "ReverbReflectionsGain" (etc.) is not defined in the XACT project.
             audioEngine.SetGlobalVariable("ReverbReflectionsGain", -27.8f); // -> IndexOutOfRangeException.

         AFTER:
             // Prefix sees "ReverbReflectionsGain" starts with "Reverb" and bypasses the call entirely.
             // No exception; game continues normally (no XACT-driven reverb applied).

         NOTES
           • This is a non-destructive shim to keep startup clean. When the developers restore the
             missing XACT globals (or correct load order), simply remove this patch.
           • Scope is intentionally narrow: only names that begin with "Reverb" are skipped.
        */

        [HarmonyPatch(typeof(AudioEngine), nameof(AudioEngine.SetGlobalVariable),
                      new[] { typeof(string), typeof(float) })]
        internal static class SkipReverbSets
        {
            private static readonly HashSet<string> Logged = new HashSet<string>(StringComparer.Ordinal);

            // Prevent the original call for any "Reverb*" global => no exception, no first-chance noise.
            static bool Prefix(string name, float value)
            {
                if (!string.IsNullOrEmpty(name) &&
                    name.StartsWith("Reverb", StringComparison.Ordinal))
                {
                    if (VoiceChatConfigStore.Current.LogReverbSkips && Logged.Add(name))
                        Log($"[Audio] Skipping buggy XACT global '{name}' (value={value}).");
                    return false; // Don't call AudioEngine.SetGlobalVariable.
                }
                return true;
            }

            static void Log(string s) => ModLoader.LogSystem.Log(s);
        }
        #endregion

        #endregion
    }
}