using System;
using System.Linq;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.logic.behaviors.@new.movements
{
    public sealed class RaftGravity : Behavior
    {
        private const float HALF_WIDTH = 2.0f;   // 4 tiles / 2
        private const float HALF_HEIGHT = 3.0f;  // 6 tiles / 2
        private const float DRIFT_SPEED = 1.5f;  // tiles/sec max drift
        private const float DEADZONE = 0.3f;     // center deadzone in tiles

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            if (host.World == null)
                return;

            // Find all players riding this raft
            var riders = host.World.Players.Values
                .Where(p => p.RidingEntityId == host.Id)
                .ToList();

            if (riders.Count == 0)
                return;

            // Compute average offset from raft center
            float avgOffsetX = 0, avgOffsetY = 0;
            foreach (var rider in riders)
            {
                avgOffsetX += rider.RaftOffsetX;
                avgOffsetY += rider.RaftOffsetY;
            }
            avgOffsetX /= riders.Count;
            avgOffsetY /= riders.Count;

            // Apply deadzone
            if (MathF.Abs(avgOffsetX) < DEADZONE) avgOffsetX = 0;
            if (MathF.Abs(avgOffsetY) < DEADZONE) avgOffsetY = 0;

            if (avgOffsetX == 0 && avgOffsetY == 0)
                return;

            // Drift proportional to offset from center
            float driftX = (avgOffsetX / HALF_WIDTH) * DRIFT_SPEED * time.DeltaTime;
            float driftY = (avgOffsetY / HALF_HEIGHT) * DRIFT_SPEED * time.DeltaTime;

            // Move raft
            host.Move(host.X + driftX, host.Y + driftY);

            // Carry all riders along
            foreach (var rider in riders)
            {
                rider.Move(host.X + rider.RaftOffsetX, host.Y + rider.RaftOffsetY);
            }
        }
    }
}
