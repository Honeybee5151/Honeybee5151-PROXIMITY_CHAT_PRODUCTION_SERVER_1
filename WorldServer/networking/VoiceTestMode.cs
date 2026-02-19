// ============================================================
// VOICE TEST MODE - Delete this file for production builds
// Allows test bots (player IDs >= 60000) to bypass auth
// and appear as nearby players for stress testing voice chat.
// ============================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace WorldServer.networking
{
    public static class VoiceTestMode
    {
        // Set to false (or delete this file) for production
        public const bool ENABLED = true;

        public const int MIN_BOT_ID = 60000;

        // Fake positions for test bots
        private static readonly ConcurrentDictionary<string, PlayerPosition> BotPositions = new();

        // Track which world bots are "in"
        private static int botWorldId = -1;

        /// <summary>Returns true if this player ID is a test bot and test mode is enabled.</summary>
        public static bool IsTestBot(string playerId)
        {
            if (!ENABLED) return false;
            return int.TryParse(playerId, out int id) && id >= MIN_BOT_ID;
        }

        /// <summary>Returns true if auth should be skipped for this player.</summary>
        public static bool ShouldSkipAuth(string playerId) => IsTestBot(playerId);

        /// <summary>
        /// Register a test bot near the first real player found.
        /// Called during auth to give the bot a fake position.
        /// </summary>
        public static void RegisterBot(string playerId, VoiceHandler voiceUtils)
        {
            if (!ENABLED) return;

            // Find the first real player's position to place bot nearby
            PlayerPosition nearPos = FindFirstRealPlayerPosition(voiceUtils);

            if (nearPos != null)
            {
                var rng = new Random(playerId.GetHashCode());
                BotPositions[playerId] = new PlayerPosition
                {
                    X = nearPos.X + (float)(rng.NextDouble() * 6.0 - 3.0), // ±3 units
                    Y = nearPos.Y + (float)(rng.NextDouble() * 6.0 - 3.0),
                    WorldId = nearPos.WorldId
                };
                botWorldId = nearPos.WorldId;
                Console.WriteLine($"[TEST_BOT] Bot {playerId} placed at ({BotPositions[playerId].X:F1}, {BotPositions[playerId].Y:F1}) in world {nearPos.WorldId}");
            }
            else
            {
                // No real players online - place at origin in world 1
                BotPositions[playerId] = new PlayerPosition { X = 50, Y = 50, WorldId = 1 };
                botWorldId = 1;
                Console.WriteLine($"[TEST_BOT] Bot {playerId} placed at default (50, 50) - no real players found");
            }
        }

        /// <summary>Get a test bot's fake position, or null if not a bot.</summary>
        public static PlayerPosition GetBotPosition(string playerId)
        {
            if (!ENABLED) return null;
            return BotPositions.TryGetValue(playerId, out var pos) ? pos : null;
        }

        /// <summary>Get all test bots within range of a position.</summary>
        public static List<VoicePlayerInfo> GetBotsInRange(float speakerX, float speakerY, float range, int worldId)
        {
            var result = new List<VoicePlayerInfo>();
            if (!ENABLED) return result;

            foreach (var kvp in BotPositions)
            {
                var pos = kvp.Value;
                if (pos.WorldId != worldId) continue;

                float dx = speakerX - pos.X;
                float dy = speakerY - pos.Y;
                float distance = (float)Math.Sqrt(dx * dx + dy * dy);

                if (distance <= range)
                {
                    result.Add(new VoicePlayerInfo
                    {
                        PlayerId = kvp.Key,
                        Client = null, // Bots don't have real game clients
                        Distance = distance
                    });
                }
            }

            return result;
        }

        /// <summary>Remove a test bot.</summary>
        public static void RemoveBot(string playerId)
        {
            BotPositions.TryRemove(playerId, out _);
        }

        private static PlayerPosition FindFirstRealPlayerPosition(VoiceHandler voiceUtils)
        {
            // Use the direct method that iterates all connected clients
            return voiceUtils.GetFirstConnectedPlayerPosition();
        }
    }
}
