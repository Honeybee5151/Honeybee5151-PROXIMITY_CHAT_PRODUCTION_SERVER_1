using WorldServer.logic.behaviors;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        private _ CryingDungeon = () => Behav()
            .Init("Beam Source Left",
                new State(
                    new RingAttack(
                        radius: 0,
                        count: 1,
                        offset: 0,
                        projectileIndex: 0,
                        angleToIncrement: 0.10,
                        fixedAngle: 90,
                        coolDown: new Cooldown(100, 0)
                    )
                )
            )
            .Init("Beam Source Right",
                new State(
                    new RingAttack(
                        radius: 0,
                        count: 1,
                        offset: 0,
                        projectileIndex: 0,
                        angleToIncrement: -0.10,
                        fixedAngle: 90,
                        coolDown: new Cooldown(100, 0)
                    )
                )
            )
            .Init("Checkerboard Pillar",
                new State(
                    new State("MoveRight",
                        new PhasedShoot(
                            projectileIndex: 0,
                            fixedAngle: 90,
                            onDurationMs: 800,
                            offDurationMs: 600,
                            fireIntervalMs: 200,
                            phasePerTile: 400,
                            reversePeriodMs: 4000
                        ),
                        new MoveLine(0.5, direction: 0, distance: 10),
                        new TimedTransition(8000, "MoveLeft")
                    ),
                    new State("MoveLeft",
                        new PhasedShoot(
                            projectileIndex: 0,
                            fixedAngle: 90,
                            onDurationMs: 800,
                            offDurationMs: 600,
                            fireIntervalMs: 200,
                            phasePerTile: 400,
                            reversePeriodMs: 4000
                        ),
                        new MoveLine(0.5, direction: 180, distance: 10),
                        new TimedTransition(8000, "MoveRight")
                    )
                )
            )
            .Init("Checkerboard Pillar H",
                new State(
                    new State("MoveDown",
                        new PhasedShoot(
                            projectileIndex: 0,
                            fixedAngle: 0,
                            onDurationMs: 800,
                            offDurationMs: 800,
                            fireIntervalMs: 200,
                            phasePerTile: 200,
                            reversePeriodMs: 8000,
                            useYAxis: true
                        ),
                        new MoveLine(0.5, direction: 90, distance: 10),
                        new TimedTransition(8000, "MoveUp")
                    ),
                    new State("MoveUp",
                        new PhasedShoot(
                            projectileIndex: 0,
                            fixedAngle: 0,
                            onDurationMs: 800,
                            offDurationMs: 800,
                            fireIntervalMs: 200,
                            phasePerTile: 200,
                            reversePeriodMs: 8000,
                            useYAxis: true
                        ),
                        new MoveLine(0.5, direction: 270, distance: 10),
                        new TimedTransition(8000, "MoveDown")
                    )
                )
            );
    }
}
