// ============================================================
// VOICE TEST MODE - Delete this file for production builds
// Allows test bots (player IDs >= 60000) to bypass auth
// and appear as nearby players for stress testing voice chat.
// ============================================================
//777592
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using WorldServer.core;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.networking
{
    public static class VoiceTestMode
    {
        // Toggleable at runtime via admin dashboard
        public static volatile bool ENABLED = false;

        public const int MIN_BOT_ID = 60000;
        public const int MAX_SPRITE_BOT_ID = 60010; // bots 60000-60010 get visible sprites

        // Fake positions for test bots
        private static readonly ConcurrentDictionary<string, PlayerPosition> BotPositions = new();

        // Track spawned game entities so we can remove them later
        private static readonly ConcurrentDictionary<string, Entity> BotEntities = new();

        // Track which world bots are "in"
        private static int botWorldId = -1;

        // Priority test: first 2 bots (60000, 60001) get priority, rest don't
        private static bool prioritySetupDone = false;
        public const int PRIORITY_BOT_COUNT = 2; // first N bots get priority

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
        public static void RegisterBot(string playerId, VoiceHandler voiceUtils, GameServer gameServer)
        {
            if (!ENABLED) return;

            // Find the first real player's position to place bots in a line
            PlayerPosition nearPos = FindFirstRealPlayerPosition(voiceUtils);

            if (nearPos != null)
            {
                // Space bots in a line along X, 8 units apart from the real player
                int botIndex = 0;
                if (int.TryParse(playerId, out int id)) botIndex = id - MIN_BOT_ID;
                float offsetX = (botIndex + 1) * 8.0f; // 8, 16, 24, ... units away
                BotPositions[playerId] = new PlayerPosition
                {
                    X = nearPos.X + offsetX,
                    Y = nearPos.Y,
                    WorldId = nearPos.WorldId
                };
                botWorldId = nearPos.WorldId;
                Console.WriteLine($"[TEST_BOT] Bot {playerId} placed at ({BotPositions[playerId].X:F1}, {BotPositions[playerId].Y:F1}) — {offsetX:F0} units from player, world {nearPos.WorldId}");

                // Spawn visible sprite for bots 60000-60010
                if (int.TryParse(playerId, out int botId) && botId <= MAX_SPRITE_BOT_ID)
                {
                    SpawnBotEntity(playerId, nearPos.X + offsetX, nearPos.Y, nearPos.WorldId, gameServer);
                }

                // Auto-setup priority for testing on first bot registration
                // Note: priority is now per-listener, so for test mode we configure each bot's settings
                if (!prioritySetupDone)
                {
                    prioritySetupDone = true;
                    // Configure priority settings for each bot (as listener)
                    foreach (var botId2 in BotPositions.Keys)
                    {
                        var settings = voiceUtils.GetPrioritySettings(botId2);
                        settings.EnablePriority = true;
                        settings.ActivationThreshold = 3;
                        settings.NonPriorityVolume = 0.1f;
                        for (int i = 0; i < PRIORITY_BOT_COUNT; i++)
                            settings.AddManualPriority(MIN_BOT_ID + i);
                    }
                    Console.WriteLine($"[TEST_BOT] Priority auto-configured for bots: enabled=true threshold=3 nonPrioVol=0.1 priorityBots=[{string.Join(",", Enumerable.Range(MIN_BOT_ID, PRIORITY_BOT_COUNT))}]");
                }
            }
            else
            {
                // No real players online - place at origin in world 1
                BotPositions[playerId] = new PlayerPosition { X = 50, Y = 50, WorldId = 1 };
                botWorldId = 1;
                Console.WriteLine($"[TEST_BOT] Bot {playerId} placed at default (50, 50) - no real players found");
            }
        }

        /// <summary>Spawn a visible game entity (Sheep) at the bot's position.</summary>
        private static void SpawnBotEntity(string playerId, float x, float y, int worldId, GameServer gameServer)
        {
            try
            {
                var world = gameServer.WorldManager.GetWorld(worldId);
                if (world == null)
                {
                    Console.WriteLine($"[TEST_BOT] Cannot spawn entity for {playerId} — world {worldId} not found");
                    return;
                }

                var gameData = gameServer.Resources.GameData;
                if (!gameData.IdToObjectType.TryGetValue("Sign", out ushort objType))
                {
                    Console.WriteLine($"[TEST_BOT] Cannot spawn entity — 'Sign' not found in game data");
                    return;
                }

                // Verify the tile exists on the map
                int tileX = (int)x;
                int tileY = (int)y;
                var tile = world.Map[tileX, tileY];
                Console.WriteLine($"[TEST_BOT] Tile at ({tileX}, {tileY}): {(tile != null ? $"type={tile.TileId}" : "NULL")} — map size: {world.Map.Width}x{world.Map.Height}");

                var entity = Entity.Resolve(gameServer, objType);
                entity.Name = $"Bot {playerId}";
                entity.Move(x, y);
                entity.Spawned = true;
                world.EnterWorld(entity);
                Console.WriteLine($"[TEST_BOT] Entity type: {entity.GetType().Name}, ObjectType: 0x{objType:X4}");

                BotEntities[playerId] = entity;
                Console.WriteLine($"[TEST_BOT] Spawned Sign for bot {playerId} at ({x:F1}, {y:F1})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TEST_BOT] Failed to spawn entity for {playerId}: {ex.Message}");
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

        /// <summary>Remove a test bot and its spawned entity.</summary>
        public static void RemoveBot(string playerId)
        {
            BotPositions.TryRemove(playerId, out _);

            if (BotEntities.TryRemove(playerId, out var entity))
            {
                try
                {
                    entity.World?.LeaveWorld(entity);
                    Console.WriteLine($"[TEST_BOT] Removed entity for bot {playerId}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TEST_BOT] Failed to remove entity for {playerId}: {ex.Message}");
                }
            }
        }

        private static PlayerPosition FindFirstRealPlayerPosition(VoiceHandler voiceUtils)
        {
            // Use the direct method that iterates all connected clients
            return voiceUtils.GetFirstConnectedPlayerPosition();
        }
    }
}
