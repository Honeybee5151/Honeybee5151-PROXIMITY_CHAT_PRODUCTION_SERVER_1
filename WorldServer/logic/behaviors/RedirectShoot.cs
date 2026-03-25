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
    /// Fires a projectile toward the player, then after it travels a set distance,
    /// fires a second projectile from that midpoint re-aimed at the player's current position.
    /// Creates a two-phase "redirect" effect.
    /// </summary>
    internal class RedirectShoot : CycleBehavior
    {
        private readonly float _radius;
        private readonly int _projectileIndex;
        private readonly int _count;
        private readonly float _shootAngle;
        private readonly float _redirectDist; // tiles before redirect
        private readonly float _predictive;
        private Cooldown _coolDown;

        /// <param name="radius">Acquire range to find player</param>
        /// <param name="count">Number of projectiles per burst</param>
        /// <param name="shootAngle">Angle between projectiles in a burst (degrees)</param>
        /// <param name="projectileIndex">Which projectile definition to use</param>
        /// <param name="redirectDist">Distance in tiles before the redirect fires</param>
        /// <param name="predictive">Chance (0-1) to use predictive aiming</param>
        /// <param name="coolDown">Cooldown between volleys</param>
        public RedirectShoot(double radius, int count = 1, double shootAngle = 0,
            int projectileIndex = 0, double redirectDist = 4, double predictive = 0,
            Cooldown coolDown = new Cooldown())
        {
            _radius = (float)radius;
            _count = count;
            _shootAngle = count == 1 ? 0 : (float)(shootAngle * Math.PI / 180);
            _projectileIndex = projectileIndex;
            _redirectDist = (float)redirectDist;
            _predictive = (float)predictive;
            _coolDown = coolDown.Normalize();
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            state = new RedirectState { CoolDown = 0, Pending = null };
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var s = state as RedirectState ?? new RedirectState { CoolDown = 0, Pending = null };

            Status = CycleStatus.NotStarted;

            if (host.HasConditionEffect(ConditionEffectIndex.Stunned))
            {
                state = s;
                return;
            }

            // Check if a pending redirect needs to fire
            if (s.Pending != null)
            {
                s.Pending.Timer -= time.ElapsedMsDelta;
                if (s.Pending.Timer <= 0)
                {
                    // Fire the redirect shot from the midpoint position
                    var player = host.GetNearestEntity(_radius, null);
                    if (player != null)
                    {
                        FireBurst(host, s.Pending.MidX, s.Pending.MidY, player.X, player.Y);
                    }
                    s.Pending = null;
                }
            }

            // Main cooldown
            if (s.CoolDown > 0)
            {
                s.CoolDown -= time.ElapsedMsDelta;
                Status = CycleStatus.InProgress;
                state = s;
                return;
            }

            var target = host.GetNearestEntity(_radius, null);
            if (target == null)
            {
                state = s;
                return;
            }

            // Fire phase 1: straight at the player
            var angle = (float)Math.Atan2(target.Y - host.Y, target.X - host.X);
            FireBurst(host, host.X, host.Y, target.X, target.Y);

            // Calculate the midpoint where redirect will fire from
            var desc = host.ObjectDesc.Projectiles[_projectileIndex];
            var projSpeed = desc.Speed / 10f; // Speed in XML is in tenths of tiles/sec
            var travelTimeMs = (int)(_redirectDist / projSpeed * 1000);

            var midX = host.X + MathF.Cos(angle) * _redirectDist;
            var midY = host.Y + MathF.Sin(angle) * _redirectDist;

            s.Pending = new PendingRedirect
            {
                Timer = travelTimeMs,
                MidX = midX,
                MidY = midY
            };

            s.CoolDown = _coolDown.Next(Random);
            Status = CycleStatus.Completed;
            state = s;
        }

        private void FireBurst(Entity host, float fromX, float fromY, float toX, float toY)
        {
            if (!host.ObjectDesc.Projectiles.TryGetValue(_projectileIndex, out var desc))
                return;

            var angle = (float)Math.Atan2(toY - fromY, toX - fromX);
            var dmg = (short)Random.Next(desc.MinDamage, desc.MaxDamage);
            var startAngle = angle - _shootAngle * (_count - 1) / 2;
            var prjId = host.GetNextBulletId(_count);

            var pkt = new EnemyShootMessage()
            {
                Spawned = host.Spawned,
                BulletId = prjId,
                OwnerId = host.Id,
                StartingPos = new Position(fromX, fromY),
                Angle = startAngle,
                Damage = dmg,
                BulletType = (byte)desc.BulletType,
                AngleInc = _shootAngle,
                NumShots = (byte)_count,
                ObjectType = host.ObjectType,
                ProjectileDesc = desc,
            };

            host.World.BroadcastEnemyShootIfVisible(pkt, host);
        }

        private class PendingRedirect
        {
            public int Timer;
            public float MidX, MidY;
        }

        private class RedirectState
        {
            public int CoolDown;
            public PendingRedirect Pending;
        }
    }
}
