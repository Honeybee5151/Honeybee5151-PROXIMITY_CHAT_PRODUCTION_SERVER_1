using System;
using Shared.resources;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.logic.behaviors
{
    /// <summary>
    /// Fires a projectile at a fixed angle with on/off phasing.
    /// Phase offset is derived from the entity's X or Y position, creating
    /// a wave pattern that drifts across a row/column of pillars.
    /// Drift direction reverses periodically.
    /// </summary>
    internal class PhasedShoot : CycleBehavior
    {
        private readonly int _projectileIndex;
        private readonly float _fixedAngle;
        private readonly int _onDurationMs;
        private readonly int _offDurationMs;
        private readonly int _fireIntervalMs;
        private readonly double _phasePerTile;
        private readonly int _reversePeriodMs;
        private readonly int _cycleDuration;
        private readonly bool _useYAxis;

        public PhasedShoot(
            int projectileIndex = 0,
            double fixedAngle = 90,
            int onDurationMs = 800,
            int offDurationMs = 800,
            int fireIntervalMs = 200,
            double phasePerTile = 200,
            int reversePeriodMs = 8000,
            bool useYAxis = false)
        {
            _projectileIndex = projectileIndex;
            _fixedAngle = (float)(fixedAngle * Math.PI / 180);
            _onDurationMs = onDurationMs;
            _offDurationMs = offDurationMs;
            _fireIntervalMs = fireIntervalMs;
            _phasePerTile = phasePerTile;
            _reversePeriodMs = reversePeriodMs;
            _cycleDuration = onDurationMs + offDurationMs;
            _useYAxis = useYAxis;
        }

        private class PhasedShootState
        {
            public long TotalElapsed;
            public int FireCooldown;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            state = new PhasedShootState();
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var s = state as PhasedShootState;
            if (s == null)
            {
                state = s = new PhasedShootState();
            }

            s.TotalElapsed += time.ElapsedMsDelta;
            s.FireCooldown -= time.ElapsedMsDelta;

            Status = CycleStatus.NotStarted;

            if (s.FireCooldown > 0)
                return;

            s.FireCooldown = _fireIntervalMs;

            // Determine drift direction (alternates every reversePeriodMs)
            int driftSign = ((int)(s.TotalElapsed / _reversePeriodMs) % 2 == 0) ? 1 : -1;

            // Phase offset based on entity position
            double phase = (_useYAxis ? host.Y : host.X) * _phasePerTile * driftSign;

            // Where are we in the on/off cycle?
            double cyclePos = ((s.TotalElapsed + phase) % _cycleDuration + _cycleDuration) % _cycleDuration;

            if (cyclePos >= _onDurationMs)
            {
                Status = CycleStatus.Completed;
                return; // in "off" phase
            }

            // Fire projectile
            if (host.HasConditionEffect(ConditionEffectIndex.Stunned))
            {
                Status = CycleStatus.Completed;
                return;
            }

            if (!host.ObjectDesc.Projectiles.TryGetValue(_projectileIndex, out var desc))
            {
                Status = CycleStatus.Completed;
                return;
            }

            var dmg = (double)Random.Next(desc.MinDamage, desc.MaxDamage);
            if (host.HasConditionEffect(ConditionEffectIndex.Weak))
                dmg /= 2;

            var prjPos = new Position(host.X, host.Y);
            var prjId = host.GetNextBulletId(1);

            var pkt = new EnemyShootMessage()
            {
                Spawned = host.Spawned,
                BulletId = prjId,
                OwnerId = host.Id,
                StartingPos = prjPos,
                Angle = _fixedAngle,
                Damage = (short)dmg,
                BulletType = (byte)desc.BulletType,
                AngleInc = 0,
                NumShots = 1,
                ObjectType = host.ObjectType,
                ProjectileDesc = desc,
            };

            host.World.BroadcastEnemyShootIfVisible(pkt, host);

            Status = CycleStatus.Completed;
        }
    }
}
