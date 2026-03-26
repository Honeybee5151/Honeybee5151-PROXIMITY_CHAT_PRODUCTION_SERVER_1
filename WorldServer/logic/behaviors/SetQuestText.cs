using System;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.logic.behaviors
{
    /// <summary>
    /// On state entry, updates the world's QuestText and broadcasts it to all players.
    /// This updates the quest objective shown in the client's top-left UI.
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

            world.QuestText = _questText;
            Console.WriteLine($"[SetQuestText] Set QuestText='{_questText}' on world '{world.IdName}'");

            var questMsg = new GlobalNotificationMessage(0, "dungeonQuest:" + _questText);
            foreach (var player in world.Players.Values)
                player.Client.SendPacket(questMsg);
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
        }
    }
}
