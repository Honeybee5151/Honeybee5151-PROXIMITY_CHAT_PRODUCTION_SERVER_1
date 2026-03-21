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
                        angleToIncrement: 0.04,
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
                        angleToIncrement: -0.04,
                        fixedAngle: 90,
                        coolDown: new Cooldown(100, 0)
                    )
                )
            );
    }
}
