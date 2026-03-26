using System.Linq;
using Shared.resources;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.logic.behaviors
{
    /// <summary>
    /// On death, checks if all Quest enemies in the world are dead.
    /// If so, spawns a target entity at the specified position, updates quest text,
    /// and broadcasts a "dungeonVictory" notification.
    /// </summary>
    internal class DungeonVictorySpawn : Behavior
    {
        private readonly string _spawnEntity;
        private readonly float _spawnX;
        private readonly float _spawnY;
        private readonly string _questText;

        public DungeonVictorySpawn(string spawnEntity, float spawnX, float spawnY, string questText = null)
        {
            _spawnEntity = spawnEntity;
            _spawnX = spawnX;
            _spawnY = spawnY;
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

            // All bosses dead — spawn the NPC
            var objType = host.GameServer.Resources.GameData.IdToObjectType[_spawnEntity];
            var entity = Entity.Resolve(host.GameServer, objType);
            entity.Move(_spawnX + 0.5f, _spawnY + 0.5f);
            world.EnterWorld(entity);

            // Update quest text if specified
            if (_questText != null)
            {
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
