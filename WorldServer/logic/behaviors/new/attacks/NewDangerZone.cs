using System;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.logic.behaviors.@new.attacks
{
    /// <summary>
    /// Danger zone — broadcasts a ShowEffect so the client renders the zone + handles damage client-side.
    /// Server only tracks cone direction (for the visual broadcast) and re-broadcasts periodically.
    /// All damage is client-authoritative (uses groundDamage like lava tiles).
    /// </summary>
    public sealed class NewDangerZone : Behavior
    {
        private readonly float HalfConeAngle; // radians
        private readonly float Range;
        private readonly uint Color;
        private readonly float TurnSpeed;

        public NewDangerZone(
            float halfConeAngleDeg = 60f,
            float range = 30f,
            int damage = 0,       // unused — damage is client-side
            int tickRateMs = 0,   // unused
            uint color = 0x8080D0FF,
            int durationMs = 0,   // unused
            float turnSpeedDegPerSec = 60f,
            ConditionEffectIndex effect = 0,  // unused
            int effectDuration = 0)           // unused
        {
            HalfConeAngle = halfConeAngleDeg * MathF.PI / 180f;
            Range = range;
            Color = color;
            TurnSpeed = turnSpeedDegPerSec * MathF.PI / 180f;
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var s = state as DangerState;
            if (s == null)
            {
                s = new DangerState();
                state = s;
            }

            // Re-broadcast visual periodically so clients see the zone
            s.BroadcastTickMs += time.ElapsedMsDelta;
            if (s.BroadcastTickMs >= 5000)
            {
                s.BroadcastTickMs = 0;
                host.World.BroadcastIfVisible(new ShowEffect()
                {
                    EffectType = EffectType.DangerZone,
                    TargetObjectId = host.Id,
                    Pos1 = new Position() { X = HalfConeAngle, Y = Range },
                    Color = new ARGB(Color),
                    Duration = 8000
                }, host);
            }
        }

        private class DangerState
        {
            public int BroadcastTickMs = 5000; // start at max so first tick broadcasts immediately
        }
    }
}
