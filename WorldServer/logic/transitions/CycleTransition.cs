using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.logic.transitions
{
    /// <summary>
    /// Transitions when any CycleBehavior in the same state reports CycleStatus.Completed.
    /// Use as a companion to JumpToPlayer or other CycleBehaviors to transition immediately on completion.
    /// </summary>
    internal class CycleTransition : Transition
    {
        public CycleTransition(string targetState)
            : base(targetState)
        {
        }

        protected override bool TickCore(Entity host, TickTime time, ref object state)
        {
            // Check if any CycleBehavior in this state has completed
            if (host.CurrentState?.Behaviors != null)
            {
                foreach (var b in host.CurrentState.Behaviors)
                {
                    if (b is CycleBehavior cb && cb.Status == CycleStatus.Completed)
                        return true;
                }
            }
            return false;
        }
    }
}
