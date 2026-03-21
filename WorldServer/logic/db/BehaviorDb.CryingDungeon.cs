using WorldServer.logic.behaviors.@new;

namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        private _ CryingDungeon = () => Behav()
            .Init("Beam Source Left",
                new State(
                    new BeamSweep(
                        projectileIndex: 0,
                        startAngle: 70,
                        endAngle: 110,
                        sweepPeriodMs: 4000,
                        fireIntervalMs: 150
                    )
                )
            )
            .Init("Beam Source Right",
                new State(
                    new BeamSweep(
                        projectileIndex: 0,
                        startAngle: 110,
                        endAngle: 70,
                        sweepPeriodMs: 4000,
                        fireIntervalMs: 150
                    )
                )
            );
    }
}
