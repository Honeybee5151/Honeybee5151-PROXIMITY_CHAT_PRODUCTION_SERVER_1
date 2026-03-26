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

            // Clear the dialogue state so the NPC can re-trigger later
            NpcDialogue.ActiveDialogues.TryRemove(player.AccountId, out _);

            if (optionId == 0) // "Yes" — teleport party to Greg's spawn position
            {
                var targetX = 44.5f;
                var targetY = 41.5f;

                TeleportPlayer(player, targetX, targetY);

                // Also teleport party members in the same world
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

                            TeleportPlayer(memberClient.Player, targetX, targetY);
                        }
                    }
                }
            }
            // Option 1 ("No") — just dismiss, player can walk back and trigger dialogue again

            return true;
        }

        private void TeleportPlayer(Player player, float x, float y)
        {
            player.Client.SendPacket(new GotoMessage(player.Id, new Position(x, y)));
            player.Move(x, y);
        }
    }
}
