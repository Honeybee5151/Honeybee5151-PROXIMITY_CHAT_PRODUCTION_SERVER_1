using WorldServer.logic;
using WorldServer.logic.behaviors;
using WorldServer.logic.behaviors.@new.attacks;
using WorldServer.logic.behaviors.@new.labels;
using WorldServer.logic.transitions;
using WorldServer.logic.loot;
using Shared.resources;

namespace WorldServer.logic.db.community
{
    public static class Behavior_Dwarfism_Giant
    {
        public static void Register(BehaviorDb db)
        {
            db.RegisterCommunity("Dwarfism Giant",
                new State(
                    new DungeonVictory(),

                    new State("idle",
                        new SetAltTexture(0),
                        new ConditionalEffect(ConditionEffectIndex.Invulnerable),
                        new Wander(0.3),
                        new PlayerWithinTransition(100, "normal1_start")
                    ),

                    // ===== NORMAL PHASE 1 (100% -> 70%) =====
                    new State("normal1_start",
                        new SetAltTexture(0),
                        new Flash(0xFFFF00, 0.5, 3),
                        new Taunt("Grrrr... You dare enter MY cavern?!"),
                        // Start orb pair + bonus orb tracker
                        new SetLabelBehavior("orbs_active", LabelType.Entity),
                        new DwarfOrbPair(leftX: 25f, rightX: 60f, topY: 20f, bottomY: 63f, centerX: 43f, touchDamage: 200, shotDamage: 70),
                        new TimedTransition(1500, "normal1")
                    ),

                    new State("normal1",
                        new SetAltTexture(0),
                        new Prioritize(
                            new Chase(3.5, range: 3, sightRange: 100),
                            new Wander(0.3)
                        ),
                        // Slash attack - 4 cardinal directions
                        new Shoot(8, count: 4, shootAngle: 90, projectileIndex: 0, fixedAngle: 0, coolDown: new Cooldown(2000)),
                        // Bonus orb spawner (checks HP thresholds)
                        new DwarfBonusOrb(centerX: 43f, topY: 18f, bottomY: 65f, touchDamage: 200, shotDamage: 70),
                        new HpLessTransition(0.70, "wolf_start")
                    ),

                    // ===== WOLF PHASE (70% -> 50%) =====
                    new State("wolf_start",
                        new SetAltTexture(1),
                        new Flash(0xFF8800, 0.5, 3),
                        new Taunt("AWOOOOOO!"),
                        // Stop orbs
                        new RemoveLabelBehavior("orbs_active", LabelType.Entity),
                        new TimedTransition(1000, "wolf_move_top")
                    ),

                    new State("wolf_move_top",
                        new SetAltTexture(1),
                        new MoveTo2(43f, 20f, speed: 6, once: true, isMapPosition: true),
                        new DwarfBonusOrb(centerX: 43f, topY: 18f, bottomY: 65f, touchDamage: 200, shotDamage: 70),
                        new TimedTransition(3000, "wolf_attack")
                    ),

                    new State("wolf_attack",
                        new SetAltTexture(1),
                        // Horizontal line of wolf shots (fixed angle 0 = right, and 180 = left)
                        new Shoot(100, count: 8, shootAngle: 3, projectileIndex: 1, fixedAngle: 0, coolDown: new Cooldown(1200)),
                        new Shoot(100, count: 8, shootAngle: 3, projectileIndex: 1, fixedAngle: 180, coolDown: new Cooldown(1200)),
                        // Left diagonal (down-left ~225)
                        new Shoot(100, count: 6, shootAngle: 3, projectileIndex: 1, fixedAngle: 225, coolDown: new Cooldown(1500)),
                        // Right diagonal (down-right ~315)
                        new Shoot(100, count: 6, shootAngle: 3, projectileIndex: 1, fixedAngle: 315, coolDown: new Cooldown(1500)),
                        // Red aimed shot at player - anti safe spot
                        new Shoot(100, count: 1, projectileIndex: 2, predictive: 0.8, coolDown: new Cooldown(800)),
                        new DwarfBonusOrb(centerX: 43f, topY: 18f, bottomY: 65f, touchDamage: 200, shotDamage: 70),
                        new HpLessTransition(0.50, "normal2_start")
                    ),

                    // ===== NORMAL PHASE 2 (50% -> 20%) =====
                    new State("normal2_start",
                        new SetAltTexture(0),
                        new Flash(0xFF4400, 0.5, 3),
                        new Taunt("You'll PAY for that!"),
                        // Restart orbs + start bouncing orb
                        new SetLabelBehavior("orbs_active", LabelType.Entity),
                        new DwarfOrbPair(leftX: 25f, rightX: 60f, topY: 20f, bottomY: 63f, centerX: 43f, touchDamage: 200, shotDamage: 70),
                        new SetLabelBehavior("bouncing_orb_active", LabelType.Entity),
                        new DwarfBouncingOrb(minX: 22f, maxX: 64f, minY: 18f, maxY: 65f, centerX: 43f, touchDamage: 200, shotDamage: 70),
                        new TimedTransition(1500, "normal2")
                    ),

                    new State("normal2",
                        new SetAltTexture(0),
                        new Prioritize(
                            new Chase(4.5, range: 2, sightRange: 100),
                            new Wander(0.4)
                        ),
                        // Slash attack - 4 cardinal directions, faster
                        new Shoot(8, count: 4, shootAngle: 90, projectileIndex: 0, fixedAngle: 0, coolDown: new Cooldown(1500)),
                        new DwarfBonusOrb(centerX: 43f, topY: 18f, bottomY: 65f, touchDamage: 200, shotDamage: 70),
                        new HpLessTransition(0.20, "poop_start")
                    ),

                    // ===== POOP PHASE (20% -> dead) =====
                    new State("poop_start",
                        new SetAltTexture(2),
                        new Flash(0x8B4513, 0.5, 5),
                        new Taunt("Uh oh... HERE IT COMES!"),
                        // Stop all orbs
                        new RemoveLabelBehavior("orbs_active", LabelType.Entity),
                        new RemoveLabelBehavior("bouncing_orb_active", LabelType.Entity),
                        new TimedTransition(1500, "poop_chase")
                    ),

                    new State("poop_chase",
                        new SetAltTexture(2),
                        new ConditionalEffect(ConditionEffectIndex.Armored),
                        new Prioritize(
                            new Chase(5, range: 1, sightRange: 100),
                            new Wander(0.5)
                        ),
                        // Drop poop - 4 directions with 0 speed (stays in place)
                        new Shoot(0, count: 4, shootAngle: 90, projectileIndex: 3, fixedAngle: 0, coolDown: new Cooldown(600)),
                        new DwarfBonusOrb(centerX: 43f, topY: 18f, bottomY: 65f, touchDamage: 200, shotDamage: 70)
                    )
                )
            );
        }
    }
}
