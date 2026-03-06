using WorldServer.logic;
using WorldServer.logic.behaviors;
using WorldServer.logic.transitions;
using WorldServer.logic.loot;
using Shared.resources;

namespace WorldServer.logic.db.community
{
    public static class Behavior_submit
    {
        public static void Register(BehaviorDb db)
        {
            db.RegisterCommunity("submit Frostscale Wyrm",
    new State(
        new State("idle",
            new Prioritize(
                new Orbit(0.5, radius: 4, acquireRange: 12),
                new Wander(0.4)
            ),
            new PlayerWithinTransition(11, "engage")
        ),
        new State("engage",
            new Prioritize(
                new Chase(0.55, range: 5, sightRange: 14),
                new Orbit(0.5, radius: 3.5, acquireRange: 12),
                new Wander(0.3)
            ),
            new Shoot(9, count: 3, shootAngle: 15, projectileIndex: 0, coolDown: new Cooldown(1800)),
            new Shoot(8, count: 5, shootAngle: 12, projectileIndex: 1, coolDown: new Cooldown(2400)),
            new HpLessTransition(0.6, "aggressive")
        ),
        new State("aggressive",
            new Taunt("The frost consumes all!"),
            new Flash(0x88CCFF, 0.4, 3),
            new ConditionalEffect(ConditionEffectIndex.Armored, false, 3000),
            new Prioritize(
                new Chase(0.7, range: 3, sightRange: 16),
                new Swirl(0.55, radius: 3, acquireRange: 12),
                new Wander(0.35)
            ),
            new Shoot(10, count: 5, shootAngle: 18, projectileIndex: 0, coolDown: new Cooldown(1400)),
            new Shoot(9, count: 7, shootAngle: 10, projectileIndex: 1, coolDown: new Cooldown(2000)),
            new Grenade(2.5, 90, range: 7, coolDown: new Cooldown(3500), color: 0x88CCFF),
            new HpLessTransition(0.3, "fury")
        ),
        new State("fury",
            new Taunt("You will be entombed in ice!"),
            new Flash(0x4488FF, 0.3, 5),
            new Prioritize(
                new Charge(1.0, range: 10, coolDown: new Cooldown(2500)),
                new Chase(0.85, range: 2, sightRange: 18),
                new Swirl(0.6, radius: 2.5, acquireRange: 14)
            ),
            new Shoot(10, count: 8, shootAngle: 45, projectileIndex: 0, coolDown: new Cooldown(1000)),
            new Shoot(9, count: 3, projectileIndex: 0, coolDown: new Cooldown(1600), predictive: 0.9),
            new Shoot(9, count: 10, shootAngle: 36, projectileIndex: 1, coolDown: new Cooldown(1800)),
            new Grenade(3, 120, range: 8, coolDown: new Cooldown(2800), color: 0x4488FF),
            new HealSelf(coolDown: new Cooldown(12000), amount: 500)
        )
    ),
    new Threshold(0.01,
        new ItemLoot("submit Emberbrand", 0.04)
    )
);
        }
    }
}
