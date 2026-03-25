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
            // ========== GREG ==========
            db.RegisterCommunity("Greg",
                new State(
                    new State("idle",
                        new SetAltTexture(0),
                        new Wander(0.3),
                        new PlayerWithinTransition(10, "walk1")
                    ),

                    // --- Chase phase (HP > 60%): walk, jump, shockwave cycle ---
                    new State("walk1",
                        new SetAltTexture(0),
                        new Prioritize(
                            new Chase(3.5, range: 4, sightRange: 12),
                            new Wander(0.3)
                        ),
                        new Shoot(7, count: 3, shootAngle: 15, projectileIndex: 0, coolDown: new Cooldown(1500)),
                        new Shoot(5, count: 1, projectileIndex: 1, coolDown: new Cooldown(2500), predictive: 0.5),
                        new TimedTransition(5000, "jump1"),
                        new HpLessTransition(0.6, "walk2"),
                        new NoPlayerWithinTransition(14, "idle")
                    ),
                    new State("jump1",
                        new SetAltTexture(1),
                        new TimedTransition(800, "land1")
                    ),
                    new State("land1",
                        new SetAltTexture(0),
                        new NewExpandingRing(maxRadius: 15f, expandDuration: 3f, ringThickness: 1.5f, damage: 80, cooldown: 0f, color: 0xFFFF4400),
                        new TimedTransition(500, "walk1")
                    ),

                    // --- Charge phase (HP 30-60%): walk, jump, bigger shockwave cycle ---
                    new State("walk2",
                        new SetAltTexture(0),
                        new Flash(0xFF8800, 0.5, 3),
                        new Prioritize(
                            new Chase(4.5, range: 3, sightRange: 15),
                            new Wander(0.4)
                        ),
                        new Shoot(7, count: 5, shootAngle: 20, projectileIndex: 0, coolDown: new Cooldown(1200)),
                        new Shoot(5, count: 2, projectileIndex: 1, coolDown: new Cooldown(2000), predictive: 0.7),
                        new TimedTransition(5000, "jump2"),
                        new HpLessTransition(0.3, "spin_start")
                    ),
                    new State("jump2",
                        new SetAltTexture(1),
                        new TimedTransition(800, "land2")
                    ),
                    new State("land2",
                        new SetAltTexture(0),
                        new NewExpandingRing(maxRadius: 18f, expandDuration: 3f, ringThickness: 1.5f, damage: 100, cooldown: 0f, color: 0xFFFF4400),
                        new TimedTransition(500, "walk2")
                    ),

                    // --- Enrage phase (HP < 30%): spin + bounce charge only ---
                    new State("spin_start",
                        new SetAltTexture(2),
                        new Flash(0xFF0000, 0.5, 5),
                        new Taunt("RAAAAGH!!"),
                        new TimedTransition(500, "spin_bounce")
                    ),
                    new State("spin_bounce",
                        new SetAltTexture(2),
                        new BounceCharge(speed: 12, range: 15, bounces: 3, chargeTimeMs: 800),
                        new Shoot(8, count: 8, shootAngle: 45, projectileIndex: 0, coolDown: new Cooldown(800)),
                        new TimedTransition(3000, "spin_pause")
                    ),
                    new State("spin_pause",
                        new SetAltTexture(2),
                        new Prioritize(
                            new Chase(5, range: 2, sightRange: 15),
                            new Wander(0.5)
                        ),
                        new Shoot(8, count: 8, shootAngle: 45, projectileIndex: 0, coolDown: new Cooldown(800)),
                        new Shoot(6, count: 3, projectileIndex: 1, coolDown: new Cooldown(1500), predictive: 0.8),
                        new TimedTransition(3000, "spin_bounce")
                    )
                )
            );

            // ========== BROG ==========
            db.RegisterCommunity("Brog",
                new State(
                    new State("idle",
                        new SetAltTexture(0),
                        new Wander(0.3),
                        new PlayerWithinTransition(10, "walk1")
                    ),

                    // --- Chase phase (HP > 60%): walk, jump, shockwave cycle ---
                    new State("walk1",
                        new SetAltTexture(0),
                        new Prioritize(
                            new Chase(3.5, range: 4, sightRange: 12),
                            new Wander(0.3)
                        ),
                        new Shoot(7, count: 3, shootAngle: 15, projectileIndex: 0, coolDown: new Cooldown(1500)),
                        new Shoot(5, count: 1, projectileIndex: 1, coolDown: new Cooldown(2500), predictive: 0.5),
                        new TimedTransition(5000, "jump1"),
                        new HpLessTransition(0.6, "walk2"),
                        new NoPlayerWithinTransition(14, "idle")
                    ),
                    new State("jump1",
                        new SetAltTexture(1),
                        new TimedTransition(800, "land1")
                    ),
                    new State("land1",
                        new SetAltTexture(0),
                        new NewExpandingRing(maxRadius: 15f, expandDuration: 3f, ringThickness: 1.5f, damage: 80, cooldown: 0f, color: 0xFFFF4400),
                        new TimedTransition(500, "walk1")
                    ),

                    // --- Charge phase (HP 30-60%): walk, jump, bigger shockwave cycle ---
                    new State("walk2",
                        new SetAltTexture(0),
                        new Flash(0xFF8800, 0.5, 3),
                        new Prioritize(
                            new Charge(7, range: 8, coolDown: new Cooldown(2000)),
                            new Chase(4.5, range: 3, sightRange: 15),
                            new Wander(0.4)
                        ),
                        new Shoot(7, count: 5, shootAngle: 20, projectileIndex: 0, coolDown: new Cooldown(1200)),
                        new Shoot(5, count: 2, projectileIndex: 1, coolDown: new Cooldown(2000), predictive: 0.7),
                        new TimedTransition(5000, "jump2"),
                        new HpLessTransition(0.3, "spin_start")
                    ),
                    new State("jump2",
                        new SetAltTexture(1),
                        new TimedTransition(800, "land2")
                    ),
                    new State("land2",
                        new SetAltTexture(0),
                        new NewExpandingRing(maxRadius: 18f, expandDuration: 3f, ringThickness: 1.5f, damage: 100, cooldown: 0f, color: 0xFFFF4400),
                        new TimedTransition(500, "walk2")
                    ),

                    // --- Enrage phase (HP < 30%): spin + bounce charge only ---
                    new State("spin_start",
                        new SetAltTexture(2),
                        new Flash(0xFF0000, 0.5, 5),
                        new Taunt("RAAAAGH!!"),
                        new TimedTransition(500, "spin_bounce")
                    ),
                    new State("spin_bounce",
                        new SetAltTexture(2),
                        new BounceCharge(speed: 12, range: 15, bounces: 3, chargeTimeMs: 800),
                        new Shoot(8, count: 8, shootAngle: 45, projectileIndex: 0, coolDown: new Cooldown(800)),
                        new TimedTransition(3000, "spin_pause")
                    ),
                    new State("spin_pause",
                        new SetAltTexture(2),
                        new Prioritize(
                            new Chase(5, range: 2, sightRange: 15),
                            new Wander(0.5)
                        ),
                        new Shoot(8, count: 8, shootAngle: 45, projectileIndex: 0, coolDown: new Cooldown(800)),
                        new Shoot(6, count: 3, projectileIndex: 1, coolDown: new Cooldown(1500), predictive: 0.8),
                        new TimedTransition(3000, "spin_bounce")
                    )
                )
            );
        }
    }
}
