using Shared;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.core.commands
{
    public abstract partial class Command
    {
        internal class RaftCmd : Command
        {
            public override RankingType RankRequirement => RankingType.Admin;
            public override string CommandName => "raft";

            protected override bool Process(Player player, TickTime time, string args)
            {
                // Toggle: dismount if already on raft
                if (player.IsOnRaft())
                {
                    player.Dismount();
                    player.SendInfo("Dismounted raft.");
                    return true;
                }

                // Spawn raft entity at player position
                if (!player.GameServer.Resources.GameData.IdToObjectType.TryGetValue("Raft", out var raftType))
                {
                    player.SendError("Raft object not found in XML.");
                    return false;
                }

                var raft = Entity.Resolve(player.GameServer, raftType);
                if (raft is not Enemy raftEnemy)
                {
                    player.SendError("Raft entity is not an Enemy type.");
                    return false;
                }

                raft.Move(player.X, player.Y);
                player.World.EnterWorld(raft);

                // Attach player to raft
                raftEnemy.IsBeingRidden = true;
                player.RidingEntityId = raft.Id;
                player.RaftOffsetX = 0;
                player.RaftOffsetY = 0;

                player.SendInfo("Boarded raft! Walk to the edges to steer.");
                return true;
            }
        }
    }
}
