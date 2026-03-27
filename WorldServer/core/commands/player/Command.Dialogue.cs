using System;
using Shared.database.party;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.logic.behaviors;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.commands.player
{
    internal class DialogueCommand : Command
    {
        public override string CommandName => "dialogue";

        protected override bool Process(Player player, TickTime time, string args)
        {
            if (!int.TryParse(args.Trim(), out var optionId))
                return false;

            var world = player.World;
            if (world == null)
                return false;

            // Clear the active dialogue state and prevent re-trigger
            NpcDialogue.ActiveDialogues.TryRemove(player.AccountId, out var npcEntityId);
            NpcDialogue.DismissedDialogues[player.AccountId] = npcEntityId;

            Console.WriteLine($"[DialogueCommand] Process called: optionId={optionId}, player={player.Name}, npcEntityId={npcEntityId}");

            if (optionId == 0) // "Yes"
            {
                var targetX = 44.5f;
                var targetY = 41.5f;

                // Teleport players FIRST so they're in range when boss spawns
                TeleportPlayer(player, time, targetX, targetY);

                var partyId = player.Client.Account.PartyId;
                if (partyId > 0)
                {
                    var party = DbPartySystem.Get(player.Client.Account.Database, partyId);
                    if (party != null)
                    {
                        foreach (var member in party.PartyMembers)
                        {
                            if (member.accid == player.AccountId)
                                continue;

                            var memberClient = player.GameServer.ConnectionManager.FindClient(member.accid);
                            if (memberClient?.Player == null || memberClient.Player.World != world)
                                continue;

                            TeleportPlayer(memberClient.Player, time, targetX, targetY);
                        }
                    }
                }

                // Set quest text
                world.QuestText = "Defeat Dwarfism Giant";
                var questMsg = new GlobalNotificationMessage(0, "dungeonQuest:Defeat Dwarfism Giant");
                foreach (var p in world.Players.Values)
                    p.Client.SendPacket(questMsg);

                // Spawn boss AFTER teleport, use a short delay so the next tick picks it up
                world.StartNewTimer(500, (w, t) =>
                {
                    SpawnDwarfismGiant(player, w);
                });
            }
            // "No" just dismisses; dismissed state is already set above

            return true;
        }

        private void TeleportPlayer(Player player, TickTime time, float x, float y)
        {
            player.TeleportPosition(time, x, y, ignoreRestrictions: true);
        }

        private void SpawnDwarfismGiant(Player player, World world)
        {
            Console.WriteLine("[DialogueCommand] SpawnDwarfismGiant called");

            // Check if one already exists to prevent duplicates
            foreach (var entity in world.Quests.Values)
            {
                if (entity.ObjectDesc?.IdName == "Dwarfism Giant" && !entity.Dead)
                {
                    Console.WriteLine("[DialogueCommand] Dwarfism Giant already exists, skipping spawn");
                    return;
                }
            }

            if (!player.GameServer.Resources.GameData.IdToObjectType.TryGetValue("Dwarfism Giant", out var objType))
            {
                Console.WriteLine("[DialogueCommand] ERROR: 'Dwarfism Giant' not found in IdToObjectType!");
                return;
            }

            Console.WriteLine($"[DialogueCommand] Resolved type 0x{objType:X4}, creating entity...");
            var boss = Entity.Resolve(player.GameServer, objType);
            if (boss == null)
            {
                Console.WriteLine("[DialogueCommand] ERROR: Entity.Resolve returned null!");
                return;
            }

            boss.Move(44.5f, 47.5f);
            world.EnterWorld(boss);
            Console.WriteLine($"[DialogueCommand] Dwarfism Giant spawned at (44.5, 47.5), Id={boss.Id}");
        }
    }
}
