using System;
using Shared.resources;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.logic.behaviors.@new
{
    public sealed class BeamSweep : Behavior
    {
        private readonly int _projectileIndex;
        private readonly float _startAngle; // radians
        private readonly float _endAngle;   // radians
        private readonly int _sweepPeriodMs;
        private readonly int _fireIntervalMs;

        /// <summary>
        /// Fires projectiles in a sweeping beam pattern that oscillates between two angles.
        /// </summary>
        /// <param name="projectileIndex">Which Projectile id to use from the entity's XML definition</param>
        /// <param name="startAngle">Sweep start angle in degrees (0=East, 90=South, 180=West, 270=North)</param>
        /// <param name="endAngle">Sweep end angle in degrees</param>
        /// <param name="sweepPeriodMs">Time for one full oscillation cycle (start→end→start) in ms</param>
        /// <param name="fireIntervalMs">Time between individual projectile shots in ms</param>
        public BeamSweep(int projectileIndex, double startAngle, double endAngle, int sweepPeriodMs, int fireIntervalMs)
        {
            _projectileIndex = projectileIndex;
            _startAngle = (float)(startAngle * Math.PI / 180.0);
            _endAngle = (float)(endAngle * Math.PI / 180.0);
            _sweepPeriodMs = sweepPeriodMs;
            _fireIntervalMs = fireIntervalMs;
        }

        // State: int[] { elapsed, fireTimer }
        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            state = new int[] { 0, 0 };
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var s = (int[])state ?? new int[] { 0, 0 };

            s[0] += time.ElapsedMsDelta; // elapsed
            s[1] -= time.ElapsedMsDelta; // fireTimer

            if (s[1] > 0)
            {
                state = s;
                return;
            }

            s[1] += _fireIntervalMs;

            if (host.HasConditionEffect(ConditionEffectIndex.Stunned))
            {
                state = s;
                return;
            }

            if (!host.ObjectDesc.Projectiles.TryGetValue(_projectileIndex, out var desc))
            {
                state = s;
                return;
            }

            // Triangle wave: oscillate 0→1→0 over sweepPeriod
            float cycle = (s[0] % _sweepPeriodMs) / (float)_sweepPeriodMs;
            float t = cycle < 0.5f ? cycle * 2f : 2f - cycle * 2f;

            float angle = _startAngle + (_endAngle - _startAngle) * t;

            var dmg = Random.Next(desc.MinDamage, desc.MaxDamage);

            var pkt = new EnemyShootMessage()
            {
                Spawned = host.Spawned,
                BulletId = host.GetNextBulletId(1),
                OwnerId = host.Id,
                StartingPos = new Position(host.X, host.Y),
                Angle = angle,
                Damage = (short)dmg,
                BulletType = (byte)desc.BulletType,
                AngleInc = 0,
                NumShots = 1,
                ObjectType = host.ObjectType,
                ProjectileDesc = desc,
            };

            host.World.BroadcastEnemyShootIfVisible(pkt, host);

            state = s;
        }
    }
}
