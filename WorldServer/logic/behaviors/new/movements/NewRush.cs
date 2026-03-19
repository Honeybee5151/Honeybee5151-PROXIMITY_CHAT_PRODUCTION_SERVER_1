using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;

namespace WorldServer.logic.behaviors.@new.movements
{
    public sealed class NewRush : Behavior
    {
        private readonly float Cooldown;
        private readonly float Speed;
        private readonly float Range;
        private readonly float Duration;

        public NewRush(float cooldown = 5.0f, float speed = 6.0f, float range = 15.0f, float duration = 2.0f)
        {
            Cooldown = cooldown;
            Speed = speed;
            Range = range;
            Duration = duration;
        }

        protected override bool TickCoreOrdered(Entity host, TickTime time, ref object state)
        {
            var s = state == null ? new RushState { CooldownLeft = Cooldown } : (RushState)state;
            if (state == null)
                state = s;

            // Currently rushing — track and chase the player
            if (s.Rushing)
            {
                s.DurationLeft -= time.DeltaTime;
                if (s.DurationLeft <= 0)
                {
                    s.Rushing = false;
                    s.CooldownLeft = Cooldown;
                    return false;
                }

                // Re-acquire target position each tick for tracking
                var target = host.World.FindPlayerTarget(host);
                if (target == null)
                {
                    s.Rushing = false;
                    s.CooldownLeft = Cooldown;
                    return false;
                }

                var targetPos = new Position(target.X, target.Y);
                host.MoveToward(ref targetPos, Speed * time.BehaviourTickTime);
                return true;
            }

            // Cooldown
            s.CooldownLeft -= time.DeltaTime;
            if (s.CooldownLeft > 0.0f)
                return false;

            var rushTarget = host.World.FindPlayerTarget(host);
            if (rushTarget == null || host.DistTo(rushTarget) > Range)
                return false;

            s.Rushing = true;
            s.DurationLeft = Duration;
            return true;
        }

        protected override void OnStateExit(Entity host, TickTime time, ref object state)
        {
            var s = state == null ? new RushState() : (RushState)state;
            s.Rushing = false;
            s.CooldownLeft = 0.0f;
        }

        class RushState
        {
            public bool Rushing;
            public float CooldownLeft;
            public float DurationLeft;
        }
    }
}
