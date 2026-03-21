using System;
using System.Linq;
using WorldServer.core.objects;
using WorldServer.core.terrain;
using WorldServer.core.worlds;

namespace WorldServer.logic.behaviors.@new.movements
{
    public sealed class RaftGravity : Behavior
    {
        // Raft walkable bounds from anchor (3x4 tile raft, bottom-center anchored)
        private const float MIN_X = -1.5f;  // left edge
        private const float MAX_X = 1.5f;   // right edge
        private const float MIN_Y = -3.0f;  // top edge
        private const float MAX_Y = 0.0f;   // bottom edge

        // Visual center of the raft (midpoint of walkable bounds)
        private const float CENTER_X = (MIN_X + MAX_X) / 2;  // 0.0
        private const float CENTER_Y = (MIN_Y + MAX_Y) / 2;  // -2.0

        private const float HALF_W = (MAX_X - MIN_X) / 2;    // 1.5
        private const float HALF_H = (MAX_Y - MIN_Y) / 2;    // 2.0

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

            // Compute average offset from raft visual center
            float avgOffsetX = 0, avgOffsetY = 0;
            foreach (var rider in riders)
            {
                avgOffsetX += rider.RaftOffsetX - CENTER_X;
                avgOffsetY += rider.RaftOffsetY - CENTER_Y;
            }
            avgOffsetX /= riders.Count;
            avgOffsetY /= riders.Count;

            // Apply deadzone
            if (MathF.Abs(avgOffsetX) < DEADZONE) avgOffsetX = 0;
            if (MathF.Abs(avgOffsetY) < DEADZONE) avgOffsetY = 0;

            if (avgOffsetX == 0 && avgOffsetY == 0)
                return;

            // Get tile speed multiplier at raft center
            float tileSpeed = GetTileSpeed(host.World, host.X, host.Y);

            // Drift proportional to offset from visual center, scaled by tile speed
            float driftX = (avgOffsetX / HALF_W) * DRIFT_SPEED * tileSpeed * time.DeltaTime;
            float driftY = (avgOffsetY / HALF_H) * DRIFT_SPEED * tileSpeed * time.DeltaTime;

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

        private static float GetTileSpeed(World world, float x, float y)
        {
            var tx = (int)x;
            var ty = (int)y;
            var tile = world.Map[tx, ty];
            if (tile == null) return 1.0f;

            // Check custom ground entries first (community dungeon tiles)
            if (world.CustomGroundEntries != null)
            {
                foreach (var entry in world.CustomGroundEntries)
                {
                    if (entry.TypeCode == tile.TileId)
                        return entry.Speed;
                }
            }

            // Fall back to standard tile speed
            return tile.TileDesc?.Speed ?? 1.0f;
        }

        private static bool IsRaftPassable(World world, float cx, float cy)
        {
            // Check bounds of visible raft area
            float left = cx + MIN_X;
            float right = cx + MAX_X;
            float top = cy + MIN_Y;
            float bot = cy + MAX_Y;
            float midX = cx + CENTER_X;
            float midY = cy + CENTER_Y;

            return world.Map.Contains(cx, cy) &&
                   world.IsPassable(left, top) &&
                   world.IsPassable(right, top) &&
                   world.IsPassable(left, bot) &&
                   world.IsPassable(right, bot) &&
                   world.IsPassable(left, midY) &&
                   world.IsPassable(right, midY) &&
                   world.IsPassable(midX, top) &&
                   world.IsPassable(midX, bot);
        }
    }
}
