using System;
using Shared.resources;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;

namespace WorldServer.logic.behaviors
{
    /// <summary>
    /// Fires projectiles from positions orbiting the host in a square pattern.
    /// No child entities needed — the host creates projectiles at calculated positions.
    /// Each "ball" is a projectile spawned at a point on the square perimeter.
    /// </summary>
    internal class SquareOrbitShoot : Behavior
    {
        private readonly float _speed;
        private readonly float _halfSize;
        private readonly int _ballCount;
        private readonly int _projectileIndex;
        private readonly int _coolDownMs;

        /// <param name="speed">Movement speed of virtual ball positions along the square</param>
        /// <param name="sideLength">Full side length of the square</param>
        /// <param name="ballCount">Number of balls evenly distributed on the square</param>
        /// <param name="projectileIndex">Which projectile definition to use from host's XML</param>
        /// <param name="coolDown">How often each ball fires a projectile (ms)</param>
        public SquareOrbitShoot(double speed = 3, double sideLength = 12, int ballCount = 16,
            int projectileIndex = 0, int coolDown = 400)
        {
            _speed = (float)speed;
            _halfSize = (float)(sideLength / 2.0);
            _ballCount = ballCount;
            _projectileIndex = projectileIndex;
            _coolDownMs = coolDown;
        }

        private void GetCorner(int index, out float rx, out float ry)
        {
            switch (index % 4)
            {
                case 0: rx = -_halfSize; ry = -_halfSize; break;
                case 1: rx = _halfSize;  ry = -_halfSize; break;
                case 2: rx = _halfSize;  ry = _halfSize;  break;
                case 3: rx = -_halfSize; ry = _halfSize;  break;
                default: rx = 0; ry = 0; break;
            }
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            state = new OrbitState
            {
                EdgeProgress = 0f,
                CurrentCorner = 0,
                CooldownLeft = 0
            };
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var s = state as OrbitState;
            if (s == null)
            {
                s = new OrbitState();
                state = s;
            }

            // Advance all ball positions along the square
            var dist = host.GetSpeed(_speed) * time.DeltaTime;
            s.EdgeProgress += dist;

            var sideLen = _halfSize * 2f;
            while (s.EdgeProgress >= sideLen)
            {
                s.EdgeProgress -= sideLen;
                s.CurrentCorner = (s.CurrentCorner + 1) % 4;
            }

            // Check cooldown
            s.CooldownLeft -= time.ElapsedMsDelta;
            if (s.CooldownLeft > 0)
                return;

            s.CooldownLeft = _coolDownMs;

            // Fire a projectile from each ball position
            var desc = host.ObjectDesc.Projectiles[_projectileIndex];
            var perimeter = sideLen * 4f;
            var spacing = perimeter / _ballCount;

            for (int i = 0; i < _ballCount; i++)
            {
                // Calculate this ball's position on the square
                var ballDist = s.EdgeProgress + spacing * i;
                // Wrap around perimeter
                while (ballDist >= perimeter)
                    ballDist -= perimeter;

                var corner = s.CurrentCorner + (int)(ballDist / sideLen);
                var edgeProg = ballDist - (int)(ballDist / sideLen) * sideLen;
                corner = corner % 4;
                var nextCorner = (corner + 1) % 4;

                GetCorner(corner, out var cx, out var cy);
                GetCorner(nextCorner, out var nx, out var ny);

                var t = edgeProg / sideLen;
                var ballX = host.X + cx + (nx - cx) * t;
                var ballY = host.Y + cy + (ny - cy) * t;

                // Fire projectile from this ball position, aimed outward from host
                var angle = (float)Math.Atan2(ballY - host.Y, ballX - host.X);
                var dmg = (short)Random.Next(desc.MinDamage, desc.MaxDamage);

                var prjId = host.GetNextBulletId(1);
                var pkt = new EnemyShootMessage()
                {
                    Spawned = host.Spawned,
                    BulletId = prjId,
                    OwnerId = host.Id,
                    StartingPos = new Position(ballX, ballY),
                    Angle = angle,
                    Damage = dmg,
                    BulletType = (byte)desc.BulletType,
                    AngleInc = 0,
                    NumShots = 1,
                    ObjectType = host.ObjectType,
                    ProjectileDesc = desc,
                };

                host.World.BroadcastEnemyShootIfVisible(pkt, host);
            }
        }

        private class OrbitState
        {
            public float EdgeProgress;
            public int CurrentCorner;
            public int CooldownLeft;
        }
    }
}
