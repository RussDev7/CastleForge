using static ModLoader.LogSystem;

namespace CastleWallsMk2
{
    internal class FeedbackRouter
    {
        #region Feedback / Logging Routing

        /// <summary>Routes a message to in-game UI or log depending on config.</summary>
        public static void SendLog(string message, bool logToFile = true)
        {
            // Determine if to show the UI feedback in-game based on the config.
            var cfg = ModConfig.LoadOrCreateDefaults();

            if (cfg.ShowInGameUIFeedback)
                SendFeedback(message, logToFile); // Send to player + log.
            else
                Log(message);                     // Log only.
        }
        #endregion
    }
}