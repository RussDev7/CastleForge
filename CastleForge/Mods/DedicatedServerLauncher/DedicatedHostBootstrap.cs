/*
SPDX-License-Identifier: GPL-3.0-or-later
*/

using DNA.CastleMinerZ;
using DNA.CastleMinerZ.UI;
using DNA.Drawing.UI;
using DNA.Net.GamerServices;
using DNA.Net.GamerServices.LidgrenProvider;
using HarmonyLib;
using System;
using System.Reflection;
using System.Threading;
using static ModLoader.LogSystem;

namespace DedicatedServerLauncher
{
    /// <summary>
    /// Dedicated bootstrap flow:
    /// 1) Detect second instance launched with -dedicated.
    /// 2) Auto-run the normal Start Game frontend flow.
    /// 3) Once main menu is available, switch to Lidgren and auto-host.
    /// </summary>
    internal static class DedicatedHostBootstrap
    {
        private static bool _armed;
        private static bool _startInvoked;
        private static bool _hostStarted;

        private static NetworkSessionStaticProvider _originalProvider;
        private static LidgrenNetworkSessionStaticProvider _lidgrenProvider;

        private static readonly MethodInfo _miStartPressed =
            AccessTools.Method(typeof(FrontEndScreen), "_startScreen_OnStartPressed");

        private static readonly FieldInfo _fiUiGroup =
            AccessTools.Field(typeof(FrontEndScreen), "_uiGroup");

        private static readonly FieldInfo _fiStartScreen =
            AccessTools.Field(typeof(FrontEndScreen), "_startScreen");

        private static readonly FieldInfo _fiMainMenu =
            AccessTools.Field(typeof(FrontEndScreen), "_mainMenu");

        public static void InitializeIfDedicated()
        {
            if (!DedicatedServerArgs.Enabled)
                return;

            _armed = true;
            Log("[Dedicated] Dedicated mode armed.");
        }

        public static void Tick()
        {
            if (!_armed || _hostStarted)
                return;

            CastleMinerZGame game = CastleMinerZGame.Instance;
            FrontEndScreen fe = game != null ? game.FrontEnd : null;
            if (game == null || fe == null)
                return;

            ScreenGroup ui = _fiUiGroup.GetValue(fe) as ScreenGroup;
            Screen startScreen = _fiStartScreen.GetValue(fe) as Screen;
            Screen mainMenu = _fiMainMenu.GetValue(fe) as Screen;

            if (ui == null)
                return;

            if (!_startInvoked && ui.CurrentScreen == startScreen)
            {
                _startInvoked = true;
                Log("[Dedicated] Invoking start-screen flow.");

                _miStartPressed.Invoke(fe, new object[] { null, EventArgs.Empty });
                return;
            }

            if (ui.CurrentScreen == mainMenu)
            {
                _hostStarted = true;
                StartDedicatedHost(game);
            }
        }

        private static void StartDedicatedHost(CastleMinerZGame game)
        {
            try
            {
                Log("[Dedicated] Switching to Lidgren provider.");

                if (_originalProvider == null)
                    _originalProvider = NetworkSession.StaticProvider;

                _lidgrenProvider = new LidgrenNetworkSessionStaticProvider();
                _lidgrenProvider.DefaultPort = DedicatedServerArgs.Port;
                _lidgrenProvider.NetworkSessionServices =
                    new DedicatedNetworkSessionServices(Guid.Empty, CastleMinerZGame.NetworkVersion);

                NetworkSession.StaticProvider = _lidgrenProvider;

                game.GameMode = GameModeTypes.Survival;
                game.InfiniteResourceMode = false;
                game.Difficulty = GameDifficultyTypes.EASY;
                game.JoinGamePolicy = JoinGamePolicy.Anyone;

                game.BeginLoadTerrain(null, true);

                game.WaitForTerrainLoad(delegate
                {
                    try
                    {
                        if (game.CurrentWorld != null)
                        {
                            game.CurrentWorld.ServerMessage = DedicatedServerArgs.Name;
                            game.CurrentWorld.ServerPassword = DedicatedServerArgs.Password;
                        }

                        game.HostGame(false, delegate (bool success)
                        {
                            if (!success)
                            {
                                Log("[Dedicated] HostGame failed.");
                                return;
                            }

                            game.TerrainServerID = game.MyNetworkGamer.Id;
                            game.StartGame();

                            Log($"[Dedicated] Server started on port {DedicatedServerArgs.Port}.");
                        });
                    }
                    catch (Exception ex)
                    {
                        Log($"[Dedicated] Host callback failed: {ex}");
                    }
                });
            }
            catch (Exception ex)
            {
                Log($"[Dedicated] Failed to start host: {ex}");
            }
        }
    }
}