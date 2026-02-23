using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Shared;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.core.commands.player
{
    internal class DungeonCommand : Command
    {
        public override string CommandName => "dungeon";
        public override string Alias => "d";

        protected override bool Process(Player player, TickTime time, string args)
        {
            var names = GetCommunityDungeonNames(player.GameServer);

            if (string.IsNullOrWhiteSpace(args))
            {
                if (names.Count == 0)
                {
                    player.SendInfo("No community dungeons available yet.");
                    return true;
                }

                player.SendInfo("Community Dungeons:");
                for (int i = 0; i < names.Count; i++)
                    player.SendInfo($"  {i + 1}. {names[i]}");
                player.SendInfo("Use /dungeon <name> to enter.");
                return true;
            }

            // Find matching dungeon (case-insensitive, partial match)
            var match = names.FirstOrDefault(n => n.Equals(args, StringComparison.OrdinalIgnoreCase));
            if (match == null)
                match = names.FirstOrDefault(n => n.StartsWith(args, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                player.SendError($"Community dungeon '{args}' not found. Use /dungeon to see the list.");
                return false;
            }

            var world = player.GameServer.WorldManager.CreateNewWorld(match, null, player.World);
            if (world == null)
            {
                player.SendError($"Failed to create dungeon: {match}");
                return false;
            }

            player.SendInfo($"Entering {match}...");
            player.Reconnect(world);
            return true;
        }

        private List<string> GetCommunityDungeonNames(GameServer server)
        {
            var path = Path.Combine(server.Resources.ResourcePath, "worlds", "community-dungeons.txt");
            if (!File.Exists(path))
                return new List<string>();

            return File.ReadAllLines(path)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l))
                .ToList();
        }
    }
}
