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

                if (raft.Id == -1)
                    raft.Id = player.World.GetNextEntityId();
                raft.Init(player.World);
                // Spawn raft so its visual center is at the player
                // Sprite is bottom-center anchored, visual center is ~2 tiles above anchor
                // (tuned bounds: X [-1.0, 0.0], Y [-4.0, 0.0], center at (-0.5, -2.0))
                raft.Move(player.X + 0.5f, player.Y + 2.0f);
                // AddToWorld directly so entity is immediately in dictionary
                // (EnterWorld queues for next tick → GetRidingEnemy() returns null → instant dismount)
                player.World.AddToWorld(raft);

                // Attach player to raft — player starts at visual center
                raftEnemy.IsBeingRidden = true;
                player.RidingEntityId = raft.Id;
                player.RaftOffsetX = -0.5f;
                player.RaftOffsetY = -2.0f;

                player.SendInfo("Boarded raft! Walk to the edges to steer.");
                return true;
            }
        }
    }
}
