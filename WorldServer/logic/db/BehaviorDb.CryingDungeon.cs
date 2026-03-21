using WorldServer.logic.behaviors;

namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        private _ CryingDungeon = () => Behav()
            .Init("Beam Source Left",
                new State(
                    // Single-projectile rotating beam (clockwise)
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
                    // Single-projectile rotating beam (counter-clockwise)
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
                    new PhasedShoot(
                        projectileIndex: 0,
                        fixedAngle: 90,
                        onDurationMs: 800,
                        offDurationMs: 800,
                        fireIntervalMs: 200,
                        phasePerTile: 200,
                        reversePeriodMs: 8000
                    )
                )
            )
            .Init("Checkerboard Pillar H",
                new State(
                    new PhasedShoot(
                        projectileIndex: 0,
                        fixedAngle: 0,
                        onDurationMs: 800,
                        offDurationMs: 800,
                        fireIntervalMs: 200,
                        phasePerTile: 200,
                        reversePeriodMs: 8000,
                        useYAxis: true
                    )
                )
            );
    }
}
