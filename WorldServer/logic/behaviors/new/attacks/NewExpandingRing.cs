using System;
using System.Collections.Generic;
using NLog;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;

namespace WorldServer.logic.behaviors.@new.attacks
{
    /// <summary>
    /// Expanding ring attack — fires visual on state entry, uses world timers for damage
    /// so it persists through state transitions.
    /// </summary>
    public sealed class NewExpandingRing : Behavior
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private readonly float MaxRadius;
        private readonly float ExpandDurationSec;
        private readonly float RingThickness;
        private readonly int Damage;
        private readonly uint Color;
        private readonly ConditionEffectIndex Effect;
        private readonly int EffectDuration;

        public NewExpandingRing(
            float maxRadius = 10.0f,
            float expandDuration = 2.0f,
            float ringThickness = 1.5f,
            int damage = 80,
            float cooldown = 8.0f,
            uint color = 0xFFFF0000,
            ConditionEffectIndex effect = 0,
            int effectDuration = 0)
        {
            MaxRadius = maxRadius;
            ExpandDurationSec = expandDuration;
            RingThickness = ringThickness;
            Damage = damage;
            Color = color;
            Effect = effect;
            EffectDuration = effectDuration;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            state = null;

            var totalMs = (int)(ExpandDurationSec * 1000);

            // Broadcast visual to clients
            host.World.BroadcastIfVisible(new ShowEffect()
            {
                EffectType = EffectType.ExpandingRing,
                TargetObjectId = host.Id,
                Pos1 = new Position() { X = MaxRadius, Y = RingThickness },
                Color = new ARGB(Color),
                Duration = totalMs
            }, host);

            // Capture state for the timer closure
            var hitPlayers = new HashSet<int>();
            var elapsedMs = 0;
            var maxRadius = MaxRadius;
            var ringThickness = RingThickness;
            var damage = Damage;
            var color = Color;
            var effect = Effect;
            var effectDuration = EffectDuration;
            var entityId = host.Id;
            var originX = host.X;
            var originY = host.Y;

            // Use Action overload — timer auto-removes after callback, we re-add manually
            void TimerTick(World world, TickTime t)
            {
                elapsedMs += t.ElapsedMsDelta;

                if (elapsedMs >= totalMs)
                    return; // done — timer auto-removes

                var entity = world.GetEntity(entityId);
                if (entity == null)
                    return; // entity gone — timer auto-removes

                var progress = elapsedMs / (float)totalMs;
                var currentRadius = progress * maxRadius;
                var ringInner = Math.Max(0f, currentRadius - ringThickness / 2f);
                var ringOuter = currentRadius + ringThickness / 2f;

                var pos = new Position(originX, originY);
                var searchRadius = maxRadius + ringThickness + 2f;

                world.AOE(pos, searchRadius, true, p =>
                {
                    if (p is not Player player)
                        return;

                    if (hitPlayers.Contains(player.Id))
                        return;

                    var dist = player.DistTo(originX, originY);

                    if (dist >= ringInner && dist <= ringOuter)
                    {
                        var hitPos = new Position(player.X, player.Y);
                        world.BroadcastIfVisible(new AoeMessage(hitPos, 1f, damage, effect, effectDuration / 1000f, entity.ObjectType, new ARGB(color)), player);

                        (p as IPlayer).Damage(damage, entity);
                        hitPlayers.Add(player.Id);

                        if (effect != 0 && !player.HasConditionEffect(ConditionEffectIndex.Invincible) && !player.HasConditionEffect(ConditionEffectIndex.Invulnerable))
                            player.ApplyConditionEffect(new ConditionEffect(effect, effectDuration));
                    }
                });

                // Re-schedule for next tick
                world.StartNewTimer(50, TimerTick);
            }

            // Start first tick after 50ms
            host.World.StartNewTimer(50, TimerTick);
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            // Damage is handled by world timers to survive state transitions
        }
    }
}
