using System.Linq;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.logic.behaviors
{
    /// <summary>
    /// On death, checks if all Quest enemies in the world are dead.
    /// If so, broadcasts a "dungeonVictory" GlobalNotification to all players.
    /// </summary>
    internal class DungeonVictory : Behavior
    {
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

            // All bosses dead — broadcast victory to all players
            var msg = new GlobalNotificationMessage(0, "dungeonVictory");
            foreach (var player in world.Players.Values)
                player.Client.SendPacket(msg);
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
        }
    }
}
