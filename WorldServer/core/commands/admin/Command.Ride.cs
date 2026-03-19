using System.Linq;
using Shared;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.core.commands
{
    public abstract partial class Command
    {
        internal class Ride : Command
        {
            public override RankingType RankRequirement => RankingType.Admin;
            public override string CommandName => "ride";

            protected override bool Process(Player player, TickTime time, string args)
            {
                if (player.IsRiding)
                {
                    player.Dismount();
                    player.SendInfo("Dismounted!");
                    return true;
                }

                // Find nearest enemy within radius 10
                var nearest = player.World.EnemiesCollision.HitTest(player.X, player.Y, 10)
                    .OfType<Enemy>()
                    .Where(e => !e.Dead && !e.IsBeingRidden)
                    .OrderBy(e => e.DistTo(player))
                    .FirstOrDefault();

                if (nearest == null)
                {
                    player.SendError("No nearby enemy to ride!");
                    return false;
                }

                nearest.IsBeingRidden = true;
                player.RidingEntityId = nearest.Id;
                player.Move(nearest.X, nearest.Y);
                player.SendInfo($"Now riding {nearest.ObjectDesc?.DisplayName ?? "enemy"}!");
                return true;
            }
        }
    }
}
