using System;
using Shared.resources;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.utils;

namespace WorldServer.logic.behaviors
{
    /// <summary>
    /// Charges toward the nearest player at high speed. When hitting a wall,
    /// re-targets the player and charges again. Repeats for a set number of bounces.
    /// </summary>
    internal class BounceCharge : CycleBehavior
    {
        private readonly float _speed;
        private readonly float _range;

        public BounceCharge(double speed = 8, float range = 100)
        {
            _speed = (float)speed;
            _range = range;
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var s = state as BounceState ?? new BounceState();

            Status = CycleStatus.NotStarted;

            if (host.HasConditionEffect(ConditionEffectIndex.Paralyzed))
            {
                state = s;
                return;
            }

            // Currently charging in a direction
            if (s.Charging)
            {
                var dist = host.GetSpeed(_speed) * time.DeltaTime;
                var newX = host.X + s.DirX * dist;
                var newY = host.Y + s.DirY * dist;

                // Only stop at NoWalk tiles and walls (FullOccupy) — plow through decorations/objects
                var hitWall = IsHardBlock(host, newX, newY);

                if (!hitWall)
                    host.MoveUnchecked(newX, newY);

                // Grace period after bounce — don't check wall hits for 300ms
                s.GraceMs -= time.ElapsedMsDelta;
                if (s.GraceMs > 0)
                {
                    Status = CycleStatus.InProgress;
                    state = s;
                    return;
                }

                if (hitWall)
                {
                    // Wall hit — re-target player and charge again (infinite bounces)
                    var player = host.GetNearestEntity(_range, null);
                    if (player != null)
                    {
                        var toX = player.X - host.X;
                        var toY = player.Y - host.Y;
                        var len = MathF.Sqrt(toX * toX + toY * toY);
                        if (len > 0.01f)
                        {
                            s.DirX = toX / len;
                            s.DirY = toY / len;
                            s.GraceMs = 300; // grace period to escape the wall
                        }
                    }
                }

                Status = CycleStatus.InProgress;
                state = s;
                return;
            }

            // Start a new bounce sequence
            if (!s.Active)
            {
                var player = host.GetNearestEntity(_range, null);
                if (player == null)
                {
                    state = s;
                    return;
                }

                var toX = player.X - host.X;
                var toY = player.Y - host.Y;
                var len = MathF.Sqrt(toX * toX + toY * toY);
                if (len < 0.01f)
                {
                    state = s;
                    return;
                }

                s.DirX = toX / len;
                s.DirY = toY / len;
                s.Charging = true;
                s.Active = true;
                s.GraceMs = 0;

                Status = CycleStatus.InProgress;
            }

            state = s;
        }

        /// <summary>
        /// Only blocks on NoWalk ground tiles and FullOccupy objects (walls).
        /// Ignores decorations, EnemyOccupySquare, etc.
        /// </summary>
        private static bool IsHardBlock(Entity host, float x, float y)
        {
            var map = host.World?.Map;
            if (map == null) return true;

            var ix = (int)x;
            var iy = (int)y;
            if (!map.Contains(ix, iy)) return true;

            var tile = map[ix, iy];
            var tileDesc = host.GameServer.Resources.GameData.Tiles[tile.TileId];
            if (tileDesc.NoWalk) return true;

            if (tile.ObjType != 0 && tile.ObjDesc != null && tile.ObjDesc.FullOccupy)
                return true;

            return false;
        }

        private class BounceState
        {
            public bool Active;
            public bool Charging;
            public float DirX;
            public float DirY;
            public int GraceMs;
        }
    }
}
