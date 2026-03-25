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
        private readonly int _bounces;

        public BounceCharge(double speed = 8, float range = 15, int bounces = 3)
        {
            _speed = (float)speed;
            _range = range;
            _bounces = bounces;
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
                var prevX = host.X;
                var prevY = host.Y;
                var dist = host.GetSpeed(_speed) * time.DeltaTime;

                host.ValidateAndMove(host.X + s.DirX * dist, host.Y + s.DirY * dist);

                // Grace period after bounce — don't check wall hits for 300ms
                // so the boss can move away from the wall it just bounced off
                s.GraceMs -= time.ElapsedMsDelta;
                if (s.GraceMs > 0)
                {
                    Status = CycleStatus.InProgress;
                    state = s;
                    return;
                }

                // Check if we hit a wall (position barely changed)
                var dx = host.X - prevX;
                var dy = host.Y - prevY;
                var movedDist = MathF.Sqrt(dx * dx + dy * dy);
                var expectedDist = dist * 0.5f;

                if (movedDist < expectedDist)
                {
                    // Wall hit — bounce
                    s.BouncesLeft--;

                    if (s.BouncesLeft <= 0)
                    {
                        // Done bouncing
                        s.Charging = false;
                        s.Active = false;
                        Status = CycleStatus.Completed;
                        state = s;
                        return;
                    }

                    // Re-target player for next bounce
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
                s.BouncesLeft = _bounces;
                s.GraceMs = 0;

                Status = CycleStatus.InProgress;
            }

            state = s;
        }

        private class BounceState
        {
            public bool Active;
            public bool Charging;
            public float DirX;
            public float DirY;
            public int BouncesLeft;
            public int GraceMs;
        }
    }
}
