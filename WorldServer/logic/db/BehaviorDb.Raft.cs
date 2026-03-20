using WorldServer.logic.behaviors.@new.movements;

namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        private _ Raft = () => Behav()
            .Init("Raft",
                new State(
                    new RaftGravity()
                )
            );
    }
}
