/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using DNA.Net.GamerServices;
using System.Reflection;
using DNA;

namespace VoiceChat
{
    /// <summary>
    /// Lightweight "speaker" overlay that shows "<Gamertag> is talking" in a corner of the screen.
    ///
    /// Lifecycle:
    ///  • <see cref="Heard(NetworkGamer)"/> is called by the VoiceChat.ProcessMessage prefix whenever
    ///    a remote voice packet is received (we ignore local/self packets upstream).
    ///  • <see cref="Tick(GameTime)"/> is called each Update to advance the fade-out timer.
    ///  • <see cref="Draw(DNAGame)"/> is called each Draw to render a small pill background + text.
    ///
    /// Config knobs (live-reload via VoiceChatConfigStore):
    ///  • ShowSpeakerHud (bool)   - Master toggle for this overlay.
    ///  • HudAnchor      (string) - TopLeft/TopRight/BottomLeft/BottomRight or TL/TR/BL/BR.
    ///  • HudSeconds     (double) - How long to show after the last packet.
    ///  • HudFadeSeconds (double) - How long the tail fade lasts at the end of HudSeconds.
    ///
    /// Implementation notes:
    ///  • Uses lazy SpriteBatch / 1x1 pixel texture creation to avoid Content dependencies.
    ///  • Does not allocate per-frame; only allocates on first draw or when font is first found.
    ///  • Font discovery is best-effort via reflection; if none is found we silently skip drawing.
    /// </summary>
    internal static class SpeakerHUD
    {
        #region State (Who Spoke + Render caches)

        private static string      _name;
        private static double      _ttl;  // Seconds remaining to display (set when Heard() is called).

        // Render bits (cached across frames; lazily created).
        private static SpriteBatch _sb;
        private static Texture2D   _px;   // 1x1 white pixel used to draw the pill background.
        private static SpriteFont  _font;

        // Static UI constants (not in config; tweak here if desired).
        private static readonly Vector2 _margin = new Vector2(10, 10); // Distance from screen edge.
        private static readonly Vector2 _pad    = new Vector2(10, 6);  // Inner padding around text.

        #endregion

        #region Public API (Called From Patches)

        /// <summary>
        /// Record that we heard a packet from this gamer and (re)start the display timer.
        /// Upstream patches already filter out local/self voice, so we only show remote talkers.
        /// </summary>
        public static void Heard(NetworkGamer g)
        {
            if (g == null || g.IsLocal) return;
            _name = g.Gamertag ?? "Player";

            // Start the timer using current config (hot-reload friendly).
            _ttl  = VoiceChatConfigStore.Current.HudSeconds;
        }

        /// <summary>
        /// Advance the timer by delta; once it reaches zero the banner stops drawing.
        /// </summary>
        public static void Tick(GameTime gt)
        {
            if (_ttl > 0) _ttl -= gt.ElapsedGameTime.TotalSeconds;
        }

        /// <summary>
        /// Draw the "<Gamertag> is talking" pill in the configured corner, fading near the end.
        /// Does nothing if disabled, expired, or no font is discoverable.
        /// </summary>
        public static void Draw(DNAGame game)
        {
            var cfg = VoiceChatConfigStore.Current;
            if (!cfg.ShowSpeakerHud) return; // User disabled HUD.
            if (_ttl <= 0 || string.IsNullOrEmpty(_name)) return;

            // Lazy init (no ContentManager dependency).
            if (_sb == null) _sb = new SpriteBatch(game.GraphicsDevice);
            if (_px == null) { _px = new Texture2D(game.GraphicsDevice, 1, 1); _px.SetData(new[] { Color.White }); }
            if (_font == null) _font = TryGetAnyFont(game);
            if (_font == null) return; // No font found; nothing to draw.

            // Compose text + measure box in pixels.
            string text  = $"{_name} is talking";
            var   textSz = _font.MeasureString(text);
            var   boxSz  = textSz + _pad * 2f;

            // Corner placement (top-left position of the background rectangle).
            var posTL = ComputeAnchorTopLeft(cfg.HudAnchor, boxSz, game.GraphicsDevice.Viewport);

            // Fade near the end of the TTL window.
            float alpha = 1f;
            double fadeTail = cfg.HudFadeSeconds;
            if (fadeTail > 0 && _ttl < fadeTail)
                alpha = (float)(_ttl / fadeTail);

            var bg = new Color(0, 0, 0) * (0.66f * alpha); // Translucent black pill.
            var fg = Color.White * alpha;                  // Text fade matches pill.

            _sb.Begin();
            _sb.Draw(_px, new Rectangle((int)posTL.X, (int)posTL.Y, (int)boxSz.X, (int)boxSz.Y), bg);
            _sb.DrawString(_font, text, posTL + _pad, fg);
            _sb.End();
        }
        #endregion

        #region Helpers

        /// <summary>
        /// Map the configured anchor to a top-left screen position for the given box size.
        /// Accepts "TopLeft", "TopRight", "BottomLeft", "BottomRight" (case-insensitive),
        /// and shorthand "TL","TR","BL","BR".
        /// </summary>
        private static Vector2 ComputeAnchorTopLeft(string anchor, Vector2 boxSize, Viewport vp)
        {
            string a = (anchor ?? "TopLeft").Trim().ToLowerInvariant();
            int vw = vp.Width, vh = vp.Height;

            // Default TopLeft.
            float x, y;

            // Normalize shorthands to full words for the switch.
            if (a == "tl")      a = "topleft";
            else if (a == "tr") a = "topright";
            else if (a == "bl") a = "bottomleft";
            else if (a == "br") a = "bottomright";

            switch (a)
            {
                case "topright":
                    x = vw - boxSize.X - _margin.X;
                    y = _margin.Y;
                    break;

                case "bottomleft":
                    x = _margin.X;
                    y = vh - boxSize.Y - _margin.Y;
                    break;

                case "bottomright":
                    x = vw - boxSize.X - _margin.X;
                    y = vh - boxSize.Y - _margin.Y;
                    break;

                // Default: TopLeft.
                default:
                    x = _margin.X;
                    y = _margin.Y;
                    break;
            }

            // Clamp to viewport (defensive for tiny windows).
            if (x < 0) x = 0;
            if (y < 0) y = 0;
            return new Vector2(x, y);
        }

        /// <summary>
        /// Best-effort attempt to find any SpriteFont already loaded by the game
        /// (avoids a hard dependency on Content). If nothing is found silently skip drawing.
        /// </summary>
        private static SpriteFont TryGetAnyFont(DNAGame game)
        {
            // Direct font field on DNAGame (fast path).
            var fields = game.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var f in fields)
            {
                if (f.FieldType == typeof(SpriteFont) && f.GetValue(game) is SpriteFont sf1)
                    return sf1;
            }

            return null;
        }
        #endregion
    }
}