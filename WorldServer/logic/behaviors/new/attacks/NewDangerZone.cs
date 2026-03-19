using System;
using System.Collections.Generic;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;

namespace WorldServer.logic.behaviors.@new.attacks
{
    /// <summary>
    /// Danger zone — the entire area around the boss is a damage zone EXCEPT
    /// a cone in the direction the boss is facing (toward its chase target).
    /// Players outside the safe cone take periodic damage.
    /// A ShowEffect is broadcast so the client can render the red overlay with cone cutout.
    /// </summary>
    public sealed class NewDangerZone : Behavior
    {
        private readonly float HalfConeAngle; // radians
        private readonly float Range;
        private readonly int Damage;
        private readonly int TickRateMs;
        private readonly uint Color;
        private readonly int DurationMs;
        private readonly float TurnSpeed; // radians per second — how fast the cone rotates
        private readonly ConditionEffectIndex Effect;
        private readonly int EffectDuration;

        public NewDangerZone(
            float halfConeAngleDeg = 70f,
            float range = 30f,
            int damage = 50,
            int tickRateMs = 500,
            uint color = 0x80FF0000,
            int durationMs = 0,
            float turnSpeedDegPerSec = 60f,
            ConditionEffectIndex effect = 0,
            int effectDuration = 0)
        {
            HalfConeAngle = halfConeAngleDeg * MathF.PI / 180f;
            Range = range;
            Damage = damage;
            TickRateMs = tickRateMs;
            Color = color;
            DurationMs = durationMs;
            TurnSpeed = turnSpeedDegPerSec * MathF.PI / 180f;
            Effect = effect;
            EffectDuration = effectDuration;
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var s = state as DangerState;
            if (s == null)
            {
                s = new DangerState();
                state = s;
            }

            // Find chase target
            var target = host.World.FindPlayerTarget(host);
            if (target == null)
            {
                // No target — deactivate visual if active
                if (s.Active)
                {
                    s.Active = false;
                }
                return;
            }

            // Slowly rotate the cone toward the target (not instant snap)
            var desiredAngle = MathF.Atan2(target.Y - host.Y, target.X - host.X);
            var angleDiff = NormalizeAngle(desiredAngle - s.FacingAngle);
            var maxRotation = TurnSpeed * (time.ElapsedMsDelta / 1000f);
            if (MathF.Abs(angleDiff) <= maxRotation)
                s.FacingAngle = desiredAngle;
            else
                s.FacingAngle += MathF.Sign(angleDiff) * maxRotation;
            s.FacingAngle = NormalizeAngle(s.FacingAngle);

            // Broadcast visual once on activation — client effect persists and tracks the boss
            if (!s.Active)
            {
                s.Active = true;

                host.World.BroadcastIfVisible(new ShowEffect()
                {
                    EffectType = EffectType.DangerZone,
                    TargetObjectId = host.Id,
                    Pos1 = new Position() { X = HalfConeAngle, Y = Range },
                    Color = new ARGB(Color),
                    Duration = 600000 // 10 minutes — effectively permanent until boss dies
                }, host);
            }

            // Tick damage timers
            s.GlobalTickMs += time.ElapsedMsDelta;
            if (s.GlobalTickMs < TickRateMs)
                return;
            s.GlobalTickMs -= TickRateMs;

            // Check all players in range
            var pos = new Position(host.X, host.Y);
            host.World.AOE(pos, Range, true, p =>
            {
                if (p is not Player player)
                    return;

                // Check if player is in the safe cone
                var angleToPlayer = MathF.Atan2(player.Y - host.Y, player.X - host.X);
                var diff = NormalizeAngle(angleToPlayer - s.FacingAngle);

                if (MathF.Abs(diff) <= HalfConeAngle)
                    return; // Player is in safe cone — no damage

                // Player is outside safe cone — damage them
                var hitPos = new Position(player.X, player.Y);
                host.World.BroadcastIfVisible(
                    new AoeMessage(hitPos, 1f, Damage, Effect, EffectDuration / 1000f, host.ObjectType, new ARGB(Color)),
                    player);

                (p as IPlayer).Damage(Damage, host);

                if (Effect != 0 &&
                    !player.HasConditionEffect(ConditionEffectIndex.Invincible) &&
                    !player.HasConditionEffect(ConditionEffectIndex.Invulnerable))
                {
                    player.ApplyConditionEffect(new ConditionEffect(Effect, EffectDuration));
                }
            });
        }

        private static float NormalizeAngle(float angle)
        {
            while (angle > MathF.PI) angle -= 2f * MathF.PI;
            while (angle < -MathF.PI) angle += 2f * MathF.PI;
            return angle;
        }

        private class DangerState
        {
            public bool Active;
            public float FacingAngle;
            public int GlobalTickMs;
        }
    }
}
