using Shared;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.logic.behaviors.@new.movements
{
    public sealed class NewChargeWithIndicator : Behavior
    {
        private readonly float Cooldown;
        private readonly float Speed;
        private readonly float Range;
        private readonly float WindupTime;
        private readonly float PathWidth;
        private readonly uint IndicatorColor;

        public NewChargeWithIndicator(
            float cooldown = 5.0f,
            float speed = 8.0f,
            float range = 15.0f,
            float windupTime = 1.0f,
            float pathWidth = 2.0f,
            uint indicatorColor = 0xFFFF0000)
        {
            Cooldown = cooldown;
            Speed = speed;
            Range = range;
            WindupTime = windupTime;
            PathWidth = pathWidth;
            IndicatorColor = indicatorColor;
        }

        protected override bool TickCoreOrdered(Entity host, TickTime time, ref object state)
        {
            var s = state == null ? new ChargeIndicatorState { CooldownLeft = Cooldown } : (ChargeIndicatorState)state;
            if (state == null)
                state = s;

            // Currently charging
            if (s.Charging)
            {
                Charge(host, s, time.BehaviourTickTime);
                return true;
            }

            // Currently in wind-up (showing indicator)
            if (s.WindingUp)
            {
                s.WindupLeft -= time.DeltaTime;
                if (s.WindupLeft <= 0)
                {
                    s.WindingUp = false;
                    s.Charging = true;
                    s.CurrentDist = host.SqDistTo(ref s.TargetPos);
                }
                return true;
            }

            // Cooldown
            s.CooldownLeft -= time.DeltaTime;
            if (s.CooldownLeft > 0.0f)
                return false;
            s.CooldownLeft = Cooldown;

            var target = host.World.FindPlayerTarget(host);
            if (target == null || host.DistTo(target) > Range)
                return false;

            // Start wind-up phase
            s.WindingUp = true;
            s.WindupLeft = WindupTime;
            s.TargetPos = new Position(target.X, target.Y);

            // Send charge path indicator to all nearby players
            host.World.BroadcastIfVisible(new ShowEffect()
            {
                EffectType = EffectType.ChargePath,
                TargetObjectId = host.Id,
                Pos1 = s.TargetPos,
                Pos2 = new Position() { X = PathWidth },
                Color = new ARGB(IndicatorColor),
                Duration = (int)(WindupTime + 0.5f)
            }, host);

            return true;
        }

        private void Charge(Entity host, ChargeIndicatorState s, float dt)
        {
            if (!host.MoveToward(ref s.TargetPos, Speed * dt))
                s.Charging = false;
            else
            {
                var dist = host.SqDistTo(ref s.TargetPos);
                if (dist >= s.CurrentDist)
                    s.Charging = false;
                else if (dist < .1f * .1f)
                    s.Charging = false;
                s.CurrentDist = dist;
            }
        }

        protected override void OnStateExit(Entity host, TickTime time, ref object state)
        {
            var s = state == null ? new ChargeIndicatorState() : (ChargeIndicatorState)state;
            s.Charging = false;
            s.WindingUp = false;
            s.CooldownLeft = 0.0f;
        }

        class ChargeIndicatorState
        {
            public bool Charging;
            public bool WindingUp;
            public float CooldownLeft;
            public float WindupLeft;
            public Position TargetPos;
            public float CurrentDist;
        }
    }
}
