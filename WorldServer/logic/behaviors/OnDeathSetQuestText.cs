using System;
using System.Linq;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.logic.behaviors
{
    /// <summary>
    /// On death, checks if all specified enemies are dead. If so, updates world.QuestText
    /// and broadcasts it to all players. Separate from DungeonVictory — purely quest text.
    /// </summary>
    internal class OnDeathSetQuestText : Behavior
    {
        private readonly string _questText;
        private readonly string[] _enemies;

        public OnDeathSetQuestText(string questText, params string[] enemies)
        {
            _questText = questText;
            _enemies = enemies;
        }

        public override void OnDeath(Entity host, ref TickTime time)
        {
            var world = host.World;
            if (world == null)
                return;

            // Check if all named enemies are dead (exclude self — we're dying)
            foreach (var name in _enemies)
            {
                var alive = world.Quests.Values
                    .Any(e => e.Id != host.Id && e.ObjectDesc?.IdName == name && !e.Dead);
                if (alive)
                    return;
            }

            world.QuestText = _questText;
            Console.WriteLine($"[OnDeathSetQuestText] Updated QuestText to '{_questText}'");

            var questMsg = new GlobalNotificationMessage(0, "dungeonQuest:" + _questText);
            foreach (var player in world.Players.Values)
                player.Client.SendPacket(questMsg);
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
        }
    }
}
