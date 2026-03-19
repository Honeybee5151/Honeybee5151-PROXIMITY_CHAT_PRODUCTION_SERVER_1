using Shared.resources;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.logic.behaviors.@new.movements
{
    /// <summary>
    /// Temporarily applies Speedy condition to boost the mob's movement speed.
    /// Works with existing Chase/Follow behaviors — just makes them faster.
    /// </summary>
    public sealed class NewRush : Behavior
    {
        private readonly float Cooldown;
        private readonly float Duration;
        private readonly float Range;

        public NewRush(float cooldown = 5.0f, float duration = 2.0f, float range = 15.0f)
        {
            Cooldown = cooldown;
            Duration = duration;
            Range = range;
        }

        protected override bool TickCoreOrdered(Entity host, TickTime time, ref object state)
        {
            var s = state == null ? new RushState { CooldownLeft = Cooldown } : (RushState)state;
            if (state == null)
                state = s;

            if (s.Rushing)
            {
                s.DurationLeft -= time.DeltaTime;
                if (s.DurationLeft <= 0)
                {
                    s.Rushing = false;
                    s.CooldownLeft = Cooldown;
                    host.RemoveCondition(ConditionEffectIndex.Speedy);
                    return false;
                }
                return false; // Don't block other behaviors — let Chase/Follow run at boosted speed
            }

            s.CooldownLeft -= time.DeltaTime;
            if (s.CooldownLeft > 0.0f)
                return false;

            var target = host.World.FindPlayerTarget(host);
            if (target == null || host.DistTo(target) > Range)
                return false;

            // Apply Speedy and let existing movement behaviors handle the rest
            s.Rushing = true;
            s.DurationLeft = Duration;
            host.ApplyConditionEffect(ConditionEffectIndex.Speedy, (int)(Duration * 1000));
            return false;
        }

        protected override void OnStateExit(Entity host, TickTime time, ref object state)
        {
            if (state is RushState s && s.Rushing)
            {
                s.Rushing = false;
                host.RemoveCondition(ConditionEffectIndex.Speedy);
            }
        }

        class RushState
        {
            public bool Rushing;
            public float CooldownLeft;
            public float DurationLeft;
        }
    }
}
