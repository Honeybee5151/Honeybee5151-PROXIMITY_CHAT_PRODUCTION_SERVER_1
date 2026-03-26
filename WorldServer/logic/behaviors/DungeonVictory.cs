using System.Linq;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.logic.behaviors
{
    /// <summary>
    /// On death, checks if all Quest enemies in the world are dead.
    /// If so, updates quest text and broadcasts a "dungeonVictory" notification.
    /// </summary>
    internal class DungeonVictory : Behavior
    {
        private readonly string _questText;

        public DungeonVictory(string questText = null)
        {
            _questText = questText;
        }

        public override void OnDeath(Entity host, ref TickTime time)
        {
            var world = host.World;
            if (world == null)
                return;

            // Check if any other Quest enemies remain (exclude self — we're dying)
            var remaining = world.Quests.Values
                .Where(e => e.Id != host.Id && !e.Dead)
                .Count();

            if (remaining > 0)
                return;

            // Update quest text if specified
            if (_questText != null)
            {
                world.ActiveQuestText = _questText;
                var questMsg = new GlobalNotificationMessage(0, "dungeonQuest:" + _questText);
                foreach (var player in world.Players.Values)
                    player.Client.SendPacket(questMsg);
            }

            // Broadcast victory
            var victoryMsg = new GlobalNotificationMessage(0, "dungeonVictory");
            foreach (var player in world.Players.Values)
                player.Client.SendPacket(victoryMsg);
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
        }
    }
}
