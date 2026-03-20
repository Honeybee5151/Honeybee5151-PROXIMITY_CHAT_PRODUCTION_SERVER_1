using System;
using System.Linq;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.logic.behaviors.@new.movements
{
    public sealed class RaftGravity : Behavior
    {
        // Raft dimensions in tiles
        private const float HALF_WIDTH = 2.0f;    // 4 tiles wide / 2
        private const float RAFT_HEIGHT = 6.0f;   // 6 tiles tall (sprite extends upward from anchor)
        private const float VISUAL_CENTER_Y = -3.0f; // visual center is 3 tiles above anchor

        private const float DRIFT_SPEED = 1.5f;   // tiles/sec max drift
        private const float DEADZONE = 0.3f;       // center deadzone in tiles

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            if (host.World == null)
                return;

            var riders = host.World.Players.Values
                .Where(p => p.RidingEntityId == host.Id)
                .ToList();

            if (riders.Count == 0)
                return;

            // Compute average offset from raft VISUAL center (not anchor)
            // Anchor is at bottom-center; visual center is 3 tiles above anchor
            float avgOffsetX = 0, avgOffsetY = 0;
            foreach (var rider in riders)
            {
                avgOffsetX += rider.RaftOffsetX;
                avgOffsetY += rider.RaftOffsetY - VISUAL_CENTER_Y; // shift so visual center = 0
            }
            avgOffsetX /= riders.Count;
            avgOffsetY /= riders.Count;

            // Apply deadzone
            if (MathF.Abs(avgOffsetX) < DEADZONE) avgOffsetX = 0;
            if (MathF.Abs(avgOffsetY) < DEADZONE) avgOffsetY = 0;

            if (avgOffsetX == 0 && avgOffsetY == 0)
                return;

            // Drift proportional to offset from visual center
            float driftX = (avgOffsetX / HALF_WIDTH) * DRIFT_SPEED * time.DeltaTime;
            float driftY = (avgOffsetY / (RAFT_HEIGHT / 2)) * DRIFT_SPEED * time.DeltaTime;

            // Try both axes, then each independently (allows sliding along walls)
            float finalX = host.X;
            float finalY = host.Y;

            if (IsRaftPassable(host.World, host.X + driftX, host.Y + driftY))
            {
                finalX = host.X + driftX;
                finalY = host.Y + driftY;
            }
            else if (driftX != 0 && IsRaftPassable(host.World, host.X + driftX, host.Y))
            {
                finalX = host.X + driftX;
            }
            else if (driftY != 0 && IsRaftPassable(host.World, host.X, host.Y + driftY))
            {
                finalY = host.Y + driftY;
            }
            else
            {
                return;
            }

            host.Move(finalX, finalY);

            foreach (var rider in riders)
            {
                rider.Move(host.X + rider.RaftOffsetX, host.Y + rider.RaftOffsetY);
            }
        }

        private static bool IsRaftPassable(World world, float cx, float cy)
        {
            // Sprite is bottom-center anchored: X [-2, +2], Y [-6, 0] from anchor
            // Check corners, edge midpoints, and center
            float top = cy - RAFT_HEIGHT;   // -6 from anchor
            float bot = cy;                  // 0 from anchor
            float left = cx - HALF_WIDTH;    // -2
            float right = cx + HALF_WIDTH;   // +2
            float midY = cy - RAFT_HEIGHT / 2; // -3 (visual center)

            return world.Map.Contains(cx, cy) &&
                   world.IsPassable(left, top) &&
                   world.IsPassable(right, top) &&
                   world.IsPassable(left, bot) &&
                   world.IsPassable(right, bot) &&
                   world.IsPassable(left, midY) &&
                   world.IsPassable(right, midY) &&
                   world.IsPassable(cx, top) &&
                   world.IsPassable(cx, bot);
        }
    }
}
