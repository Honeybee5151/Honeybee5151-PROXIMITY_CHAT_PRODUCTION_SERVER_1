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
            // ========== GREG (club thrower only) ==========
            db.RegisterCommunity("Greg",
                new State(
                    new SpawnOnDeath("Greg Imprint"),
                    new DestroyOnDeath("Greg Ball"),
                    new State("idle",
                        new SetAltTexture(0),
                        new ConditionalEffect(ConditionEffectIndex.Invulnerable),
                        new Wander(0.3),
                        new PlayerWithinTransition(100, "chase")
                    ),

                    new State("chase",
                        new SetAltTexture(0),
                        new ConditionalEffect(ConditionEffectIndex.Invulnerable),
                        new Prioritize(
                            new Chase(2.5, range: 6, sightRange: 100),
                            new Wander(0.3)
                        ),
                        new Shoot(100, count: 1, projectileIndex: 2, coolDown: new Cooldown(4000), predictive: 0.9),
                        new NoPlayerWithinTransition(100, "idle"),
                        // When Brog dies (no longer exists in the world), Greg enrages
                        new EntityNotExistsTransition("Brog", 1000, "enrage_scream")
                    ),

                    // --- Enrage: Brog is dead ---
                    new State("enrage_scream",
                        new SetAltTexture(0),
                        new Flash(0xFF0000, 0.3, 5),
                        new Taunt("AHHHHHHH"),
                        new TimedTransition(1500, "enrage_jump")
                    ),

                    new State("enrage_jump",
                        // Jump frames 1-5 cycle based on travel time; passes through no-walk tiles
                        new JumpToPlayer(speed: 8, range: 200, texMin: 1, texMax: 5),
                        new CycleTransition("enrage_land"), // fires immediately when jump completes
                        new TimedTransition(5000, "enrage_land") // safety cap
                    ),

                    new State("enrage_land",
                        new SetAltTexture(0),
                        new Flash(0xFF0000, 0.5, 3),
                        new NewExpandingRing(maxRadius: 12f, expandDuration: 2f, ringThickness: 1.5f, damage: 200, color: 0xFFFF2200, effect: ConditionEffectIndex.Slowed, effectDuration: 3000),
                        // Spawn 16 balls that orbit Greg in a square (one-time only)
                        new Spawn("Greg Ball", maxChildren: 16, initialSpawn: 1, coolDown: new Cooldown(50)),
                        new TimedTransition(2500, "enrage_chase")
                    ),

                    new State("enrage_chase",
                        new SetAltTexture(0),
                        new Prioritize(
                            new Chase(5, range: 3, sightRange: 100),
                            new Wander(0.5)
                        ),
                        // Star burst — 5 arms (72° apart), 5 shots per arm (stacked with small angle offsets)
                        new Shoot(8, count: 5, shootAngle: 72, projectileIndex: 0, angleOffset: 0, coolDown: new Cooldown(1500)),
                        new Shoot(8, count: 5, shootAngle: 72, projectileIndex: 0, angleOffset: 4, coolDown: new Cooldown(1500)),
                        new Shoot(8, count: 5, shootAngle: 72, projectileIndex: 0, angleOffset: 8, coolDown: new Cooldown(1500)),
                        new Shoot(8, count: 5, shootAngle: 72, projectileIndex: 0, angleOffset: -4, coolDown: new Cooldown(1500)),
                        new Shoot(8, count: 5, shootAngle: 72, projectileIndex: 0, angleOffset: -8, coolDown: new Cooldown(1500)),
                        // 3-shot burst aimed at the player
                        new Shoot(8, count: 3, shootAngle: 10, projectileIndex: 0, predictive: 0.8, coolDown: new Cooldown(2000)),
                        new TimedTransition(4000, "enrage_jump_loop"),
                        new HpLessTransition(0.15, "enrage_desperation_jump")
                    ),

                    new State("enrage_jump_loop",
                        // Jump frames 1-5 cycle based on travel time; passes through no-walk tiles
                        new JumpToPlayer(speed: 10, range: 200, texMin: 1, texMax: 5),
                        new CycleTransition("enrage_land_loop"), // fires immediately when jump completes
                        new TimedTransition(5000, "enrage_land_loop") // safety cap
                    ),

                    new State("enrage_land_loop",
                        new SetAltTexture(0),
                        new Flash(0xFF0000, 0.5, 3),
                        new NewExpandingRing(maxRadius: 15f, expandDuration: 2f, ringThickness: 1.5f, damage: 200, color: 0xFFFF2200, effect: ConditionEffectIndex.Slowed, effectDuration: 3000),
                        new TimedTransition(2500, "enrage_chase"),
                        new HpLessTransition(0.15, "enrage_desperation_jump")
                    ),

                    // --- Desperation (<15% HP): double shockwave on every landing ---
                    new State("enrage_desperation_jump",
                        new JumpToPlayer(speed: 12, range: 200, texMin: 1, texMax: 5),
                        new CycleTransition("enrage_desperation_land"),
                        new TimedTransition(5000, "enrage_desperation_land")
                    ),

                    new State("enrage_desperation_land",
                        new SetAltTexture(0),
                        new Flash(0xFF0000, 0.3, 6),
                        // Double shockwave — fast inner ring + slower outer ring
                        new NewExpandingRing(maxRadius: 10f, expandDuration: 1.5f, ringThickness: 2f, damage: 200, color: 0xFFFF0000, effect: ConditionEffectIndex.Slowed, effectDuration: 3000),
                        new NewExpandingRing(maxRadius: 18f, expandDuration: 3f, ringThickness: 2f, damage: 200, color: 0xFFFF2200, effect: ConditionEffectIndex.Slowed, effectDuration: 3000),
                        new TimedTransition(3500, "enrage_desperation_chase")
                    ),

                    new State("enrage_desperation_chase",
                        new SetAltTexture(0),
                        new Prioritize(
                            new Chase(6, range: 2, sightRange: 100),
                            new Wander(0.5)
                        ),
                        new Shoot(8, count: 5, shootAngle: 72, projectileIndex: 0, angleOffset: 0, coolDown: new Cooldown(1200)),
                        new Shoot(8, count: 5, shootAngle: 72, projectileIndex: 0, angleOffset: 4, coolDown: new Cooldown(1200)),
                        new Shoot(8, count: 5, shootAngle: 72, projectileIndex: 0, angleOffset: 8, coolDown: new Cooldown(1200)),
                        new Shoot(8, count: 5, shootAngle: 72, projectileIndex: 0, angleOffset: -4, coolDown: new Cooldown(1200)),
                        new Shoot(8, count: 5, shootAngle: 72, projectileIndex: 0, angleOffset: -8, coolDown: new Cooldown(1200)),
                        new Shoot(8, count: 3, shootAngle: 10, projectileIndex: 0, predictive: 0.9, coolDown: new Cooldown(1500)),
                        new TimedTransition(3000, "enrage_desperation_jump")
                    )
                )
            );

            // ========== BROG ==========
            db.RegisterCommunity("Brog",
                new State(
                    new SpawnOnDeath("Brog Imprint"),
                    new State("idle",
                        new SetAltTexture(0),
                        new Wander(0.3),
                        new PlayerWithinTransition(100, "walk1")
                    ),

                    // --- Chase phase (HP > 60%): walk, jump, shockwave cycle ---
                    new State("walk1",
                        new SetAltTexture(0),
                        new Prioritize(
                            new Chase(3.5, range: 4, sightRange: 100),
                            new Wander(0.3)
                        ),
                        new Shoot(5, count: 1, projectileIndex: 1, coolDown: new Cooldown(2500), predictive: 0.5),
                        // Boomerang boulders — travel out and return, 2x range
                        new Shoot(7, count: 3, shootAngle: 15, projectileIndex: 0, coolDown: new Cooldown(1500), predictive: 0.5),
                        new TimedTransition(5000, "jump1"),
                        new HpLessTransition(0.6, "walk2"),
                        new NoPlayerWithinTransition(100, "idle")
                    ),
                    new State("jump1",
                        new SetAltTexture(1),
                        new TimedTransition(800, "land1")
                    ),
                    new State("land1",
                        new SetAltTexture(0),
                        new NewExpandingRing(maxRadius: 15f, expandDuration: 3f, ringThickness: 1.5f, damage: 200, color: 0xFFFF4400, effect: ConditionEffectIndex.Slowed, effectDuration: 3000),
                        new TimedTransition(3500, "walk1")
                    ),

                    // --- Charge phase (HP 30-60%): walk, jump, bigger shockwave cycle ---
                    new State("walk2",
                        new SetAltTexture(0),
                        new Flash(0xFF8800, 0.5, 3),
                        new Prioritize(
                            new Charge(7, range: 8, coolDown: new Cooldown(2000)),
                            new Chase(4.5, range: 3, sightRange: 100),
                            new Wander(0.4)
                        ),
                        new Shoot(5, count: 2, projectileIndex: 1, coolDown: new Cooldown(2000), predictive: 0.7),
                        // Boomerang boulders — faster in phase 2
                        new Shoot(7, count: 5, shootAngle: 15, projectileIndex: 0, coolDown: new Cooldown(1200), predictive: 0.7),
                        new TimedTransition(5000, "jump2"),
                        new HpLessTransition(0.3, "spin_start")
                    ),
                    new State("jump2",
                        new SetAltTexture(1),
                        new TimedTransition(800, "land2")
                    ),
                    new State("land2",
                        new SetAltTexture(0),
                        new NewExpandingRing(maxRadius: 18f, expandDuration: 3f, ringThickness: 1.5f, damage: 200, color: 0xFFFF4400, effect: ConditionEffectIndex.Slowed, effectDuration: 3000),
                        new TimedTransition(3500, "walk2")
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
                        new BounceCharge(speed: 12, range: 100),
                        new Shoot(8, count: 8, shootAngle: 45, projectileIndex: 0, coolDown: new Cooldown(800))
                    )
                )
            );

            // ========== GREG BALL (orbiting minion — unkillable) ==========
            db.RegisterCommunity("Greg Ball",
                new State(
                    new State("orbit",
                        // Each ball gets a unique corner via entity ID, orbits Greg in a square
                        new SquareOrbit(speed: 3, sideLength: 12, startCorner: -1, acquireRange: 100, target: "Greg"),
                        // Hidden projectile for contact damage (Size 1 = invisible, Speed 15 for client hit detection)
                        new Shoot(1, count: 1, projectileIndex: 0, coolDown: new Cooldown(400))
                    )
                )
            );
        }
    }
}
