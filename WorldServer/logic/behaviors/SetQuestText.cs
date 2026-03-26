using System;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.logic.behaviors
{
    /// <summary>
    /// On state entry, sets the world's ActiveQuestText and broadcasts it to all players.
    /// Used to advance quest objectives when an NPC becomes interactable.
    /// </summary>
    internal class SetQuestText : Behavior
    {
        private readonly string _questText;

        public SetQuestText(string questText)
        {
            _questText = questText;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            var world = host.World;
            if (world == null)
                return;

            world.ActiveQuestText = _questText;
            Console.WriteLine($"[SetQuestText] Set ActiveQuestText='{_questText}' on world '{world.IdName}'");

            var questMsg = new GlobalNotificationMessage(0, "dungeonQuest:" + _questText);
            foreach (var player in world.Players.Values)
                player.Client.SendPacket(questMsg);
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
        }
    }
}
