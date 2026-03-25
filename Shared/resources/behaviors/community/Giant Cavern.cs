using WorldServer.logic;
using WorldServer.logic.behaviors;
using WorldServer.logic.behaviors.@new.attacks;
using WorldServer.logic.transitions;
using WorldServer.logic.loot;
using Shared.resources;

namespace WorldServer.logic.db.community
{
    public static class Behavior_Giant_Cavern
    {
        public static void Register(BehaviorDb db)
        {
            db.RegisterCommunity("Greg",
                new State(
                    new State("idle",
                        new SetAltTexture(0),
                        new Wander(0.3),
                        new PlayerWithinTransition(10, "chase")
                    ),
                    new State("chase",
                        new SetAltTexture(0),
                        new Prioritize(
                            new Chase(3.5, range: 4, sightRange: 12),
                            new Wander(0.3)
                        ),
                        new Shoot(7, count: 3, shootAngle: 15, projectileIndex: 0, coolDown: new Cooldown(1500)),
                        new Shoot(5, count: 1, projectileIndex: 1, coolDown: new Cooldown(2500), predictive: 0.5),
                        new TimedTransition(5000, "jump_up_chase"),
                        new HpLessTransition(0.6, "charge"),
                        new NoPlayerWithinTransition(14, "idle")
                    ),
                    new State("jump_up_chase",
                        new SetAltTexture(1),
                        new TimedTransition(800, "jump_land_chase")
                    ),
                    new State("jump_land_chase",
                        new SetAltTexture(0),
                        new NewExpandingRing(maxRadius: 15f, expandDuration: 3f, ringThickness: 1.5f, damage: 80, cooldown: 99f, color: 0xFFFF4400),
                        new TimedTransition(300, "chase")
                    ),
                    new State("charge",
                        new SetAltTexture(1),
                        new Prioritize(
                            new Chase(5, range: 2, sightRange: 15),
                            new Wander(0.4)
                        ),
                        new Shoot(6, count: 5, shootAngle: 25, projectileIndex: 0, coolDown: new Cooldown(1200)),
                        new Shoot(5, count: 2, projectileIndex: 1, coolDown: new Cooldown(2000), predictive: 0.7),
                        new TimedTransition(5000, "jump_up_charge"),
                        new HpLessTransition(0.3, "enrage")
                    ),
                    new State("jump_up_charge",
                        new SetAltTexture(1),
                        new TimedTransition(800, "jump_land_charge")
                    ),
                    new State("jump_land_charge",
                        new SetAltTexture(0),
                        new NewExpandingRing(maxRadius: 18f, expandDuration: 3f, ringThickness: 1.5f, damage: 100, cooldown: 99f, color: 0xFFFF4400),
                        new TimedTransition(300, "charge")
                    ),
                    new State("enrage",
                        new SetAltTexture(2),
                        new Flash(0xFF0000, 0.5, 5),
                        new Prioritize(
                            new Chase(5, range: 2, sightRange: 15),
                            new Wander(0.5)
                        ),
                        new Shoot(8, count: 8, shootAngle: 45, projectileIndex: 0, coolDown: new Cooldown(800)),
                        new Shoot(6, count: 3, projectileIndex: 1, coolDown: new Cooldown(1500), predictive: 0.8),
                        new TimedTransition(5000, "jump_up_enrage")
                    ),
                    new State("jump_up_enrage",
                        new SetAltTexture(1),
                        new TimedTransition(800, "jump_land_enrage")
                    ),
                    new State("jump_land_enrage",
                        new SetAltTexture(2),
                        new NewExpandingRing(maxRadius: 20f, expandDuration: 3f, ringThickness: 2f, damage: 120, cooldown: 99f, color: 0xFFFF0000),
                        new TimedTransition(300, "enrage")
                    )
                )
            );

            db.RegisterCommunity("Brog",
                new State(
                    new State("idle",
                        new SetAltTexture(0),
                        new Wander(0.3),
                        new PlayerWithinTransition(10, "chase")
                    ),
                    new State("chase",
                        new SetAltTexture(0),
                        new Prioritize(
                            new Chase(3.5, range: 4, sightRange: 12),
                            new Wander(0.3)
                        ),
                        new Shoot(7, count: 3, shootAngle: 15, projectileIndex: 0, coolDown: new Cooldown(1500)),
                        new Shoot(5, count: 1, projectileIndex: 1, coolDown: new Cooldown(2500), predictive: 0.5),
                        new TimedTransition(5000, "jump_up_chase"),
                        new HpLessTransition(0.6, "charge"),
                        new NoPlayerWithinTransition(14, "idle")
                    ),
                    new State("jump_up_chase",
                        new SetAltTexture(1),
                        new TimedTransition(800, "jump_land_chase")
                    ),
                    new State("jump_land_chase",
                        new SetAltTexture(0),
                        new NewExpandingRing(maxRadius: 15f, expandDuration: 3f, ringThickness: 1.5f, damage: 80, cooldown: 99f, color: 0xFFFF4400),
                        new TimedTransition(300, "chase")
                    ),
                    new State("charge",
                        new SetAltTexture(1),
                        new Prioritize(
                            new Charge(7, range: 8, coolDown: new Cooldown(2000)),
                            new Chase(4, range: 3, sightRange: 14),
                            new Wander(0.4)
                        ),
                        new Shoot(6, count: 5, shootAngle: 25, projectileIndex: 0, coolDown: new Cooldown(1200)),
                        new Shoot(5, count: 2, projectileIndex: 1, coolDown: new Cooldown(2000), predictive: 0.7),
                        new TimedTransition(5000, "jump_up_charge"),
                        new HpLessTransition(0.3, "enrage")
                    ),
                    new State("jump_up_charge",
                        new SetAltTexture(1),
                        new TimedTransition(800, "jump_land_charge")
                    ),
                    new State("jump_land_charge",
                        new SetAltTexture(0),
                        new NewExpandingRing(maxRadius: 18f, expandDuration: 3f, ringThickness: 1.5f, damage: 100, cooldown: 99f, color: 0xFFFF4400),
                        new TimedTransition(300, "charge")
                    ),
                    new State("enrage",
                        new SetAltTexture(2),
                        new Flash(0xFF0000, 0.5, 5),
                        new Prioritize(
                            new Chase(5, range: 2, sightRange: 15),
                            new Wander(0.5)
                        ),
                        new Shoot(8, count: 8, shootAngle: 45, projectileIndex: 0, coolDown: new Cooldown(800)),
                        new Shoot(6, count: 3, projectileIndex: 1, coolDown: new Cooldown(1500), predictive: 0.8),
                        new TimedTransition(5000, "jump_up_enrage")
                    ),
                    new State("jump_up_enrage",
                        new SetAltTexture(1),
                        new TimedTransition(800, "jump_land_enrage")
                    ),
                    new State("jump_land_enrage",
                        new SetAltTexture(2),
                        new NewExpandingRing(maxRadius: 20f, expandDuration: 3f, ringThickness: 2f, damage: 120, cooldown: 99f, color: 0xFFFF0000),
                        new TimedTransition(300, "enrage")
                    )
                )
            );
        }
    }
}
