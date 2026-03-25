using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Shared;
using Shared.database.party;
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
            var communityNames = GetDungeonNames(player.GameServer, "community-dungeons.txt");
            var officialNames = GetDungeonNames(player.GameServer, "official-dungeons.txt");
            var allNames = new List<string>(communityNames);
            allNames.AddRange(officialNames);

            if (string.IsNullOrWhiteSpace(args))
            {
                if (allNames.Count == 0)
                {
                    player.SendInfo("No dungeons available yet.");
                    return true;
                }

                if (officialNames.Count > 0)
                {
                    player.SendInfo("Official Dungeons:");
                    for (int i = 0; i < officialNames.Count; i++)
                        player.SendInfo($"  {i + 1}. {officialNames[i]}");
                }
                if (communityNames.Count > 0)
                {
                    player.SendInfo("Community Dungeons:");
                    for (int i = 0; i < communityNames.Count; i++)
                        player.SendInfo($"  {i + 1}. {communityNames[i]}");
                }
                player.SendInfo("Use /dungeon <name> to enter.");
                return true;
            }

            // Parse difficulty suffix: "DungeonName::2"
            var difficulty = DungeonDifficulty.Peaceful;
            var dungeonArg = args;
            var sepIndex = args.IndexOf("::");
            if (sepIndex >= 0)
            {
                var diffStr = args.Substring(sepIndex + 2);
                dungeonArg = args.Substring(0, sepIndex);
                if (int.TryParse(diffStr, out var diffVal) && diffVal >= 0 && diffVal <= 3)
                    difficulty = (DungeonDifficulty)diffVal;
            }

            // Find matching dungeon (case-insensitive, partial match)
            var match = allNames.FirstOrDefault(n => n.Equals(dungeonArg, StringComparison.OrdinalIgnoreCase));
            if (match == null)
                match = allNames.FirstOrDefault(n => n.StartsWith(dungeonArg, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                player.SendError($"Dungeon '{dungeonArg}' not found. Use /dungeon to see the list.");
                return false;
            }

            var world = player.GameServer.WorldManager.CreateNewWorld(match, null, player.World);
            if (world == null)
            {
                player.SendError($"Failed to create dungeon: {match}");
                return false;
            }

            // Set difficulty for official dungeons
            var isOfficial = officialNames.Contains(match, StringComparer.OrdinalIgnoreCase);
            if (isOfficial)
            {
                world.IsOfficialDungeon = true;
                world.DungeonDifficultyLevel = difficulty;
            }

            var diffLabel = isOfficial && difficulty != DungeonDifficulty.Peaceful
                ? $" [{difficulty}]"
                : "";
            player.SendInfo($"Entering {match}{diffLabel}...");
            var previousWorld = player.World;
            player.Reconnect(world);
            ReconnectPartyMembers(player, world, previousWorld);
            return true;
        }

        private void ReconnectPartyMembers(Player leader, World targetWorld, World previousWorld)
        {
            if (!targetWorld.IsCommunityDungeon && !targetWorld.IsOfficialDungeon)
                return;

            var partyId = leader.Client.Account.PartyId;
            if (partyId == 0)
                return;

            var party = DbPartySystem.Get(leader.Client.Account.Database, partyId);
            if (party == null)
                return;

            if (party.PartyLeader.Item1 != leader.Client.Account.Name || party.PartyLeader.Item2 != leader.Client.Account.AccountId)
                return;

            var diffLabel = targetWorld.IsOfficialDungeon
                ? $" (Difficulty: {targetWorld.DungeonDifficultyLevel})"
                : "";

            foreach (var member in party.PartyMembers)
            {
                if (member.accid == leader.AccountId)
                    continue;

                var memberClient = leader.GameServer.ConnectionManager.FindClient(member.accid);
                if (memberClient?.Player == null)
                    continue;

                if (memberClient.Player.World != previousWorld)
                    continue;

                if (targetWorld.IsPlayersMax())
                    break;

                memberClient.Player.SendInfo($"Your party leader entered a dungeon!{diffLabel}");
                memberClient.Player.Reconnect(targetWorld);
            }
        }

        private List<string> GetDungeonNames(GameServer server, string filename)
        {
            var path = Path.Combine(server.Resources.ResourcePath, "worlds", filename);
            if (!File.Exists(path))
                return new List<string>();

            return File.ReadAllLines(path)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l))
                .ToList();
        }
    }
}
