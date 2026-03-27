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
    /// Expanding ring attack — fires once on state entry, expands outward, damages players on the ring edge.
    /// Damage checking runs via world timers so it persists even if the state transitions away immediately.
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
            float cooldown = 8.0f, // kept for API compat but ignored
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
            state = null; // no state needed — damage runs via world timers

            // Broadcast visual to clients
            host.World.BroadcastIfVisible(new ShowEffect()
            {
                EffectType = EffectType.ExpandingRing,
                TargetObjectId = host.Id,
                Pos1 = new Position() { X = MaxRadius, Y = RingThickness },
                Color = new ARGB(Color),
                Duration = (int)(ExpandDurationSec * 1000)
            }, host);

            // Schedule damage checks via world timers (survives state transitions)
            var totalMs = (int)(ExpandDurationSec * 1000);
            var hitPlayers = new HashSet<int>();
            var elapsedMs = 0;
            var prevOuterEdge = 0f;
            var maxRadius = MaxRadius;
            var ringThickness = RingThickness;
            var damage = Damage;
            var color = Color;
            var effect = Effect;
            var effectDuration = EffectDuration;
            var entityId = host.Id;
            // Capture position at creation time — client visual is anchored here
            var originX = host.X;
            var originY = host.Y;

            WorldTimer timer = null;
            timer = host.World.StartNewTimer(100, (world, t) =>
            {
                elapsedMs += t.ElapsedMsDelta;

                if (elapsedMs >= totalMs)
                    return true; // done, remove timer

                // Check if entity is still alive
                var entity = world.GetEntity(entityId);
                if (entity == null)
                    return true; // entity gone, remove timer

                // Use original position (matches client visual which is anchored at creation pos)
                var centerX = originX;
                var centerY = originY;

                var progress = elapsedMs / (float)totalMs;
                var currentRadius = progress * maxRadius;
                var outerEdge = currentRadius + ringThickness / 2f;

                // Swept area: from previous tick's inner edge to current tick's outer edge
                var sweepInner = Math.Max(0f, prevOuterEdge - ringThickness);
                var sweepOuter = outerEdge;

                var pos = new Position(centerX, centerY);
                var searchRadius = maxRadius + ringThickness + 2f;
                world.AOE(pos, searchRadius, true, p =>
                {
                    if (p is not Player player)
                        return;

                    var dist = player.DistTo(centerX, centerY);
                    if (hitPlayers.Contains(player.Id))
                        return;

                    if (dist >= sweepInner && dist <= sweepOuter)
                    {
                        var hitPos = new Position(player.X, player.Y);
                        world.BroadcastIfVisible(new AoeMessage(hitPos, 1f, damage, effect, effectDuration / 1000f, entity.ObjectType, new ARGB(color)), player);

                        (p as IPlayer).Damage(damage, entity);
                        hitPlayers.Add(player.Id);

                        if (effect != 0 && !player.HasConditionEffect(ConditionEffectIndex.Invincible) && !player.HasConditionEffect(ConditionEffectIndex.Invulnerable))
                            player.ApplyConditionEffect(new ConditionEffect(effect, effectDuration));
                    }
                });

                prevOuterEdge = outerEdge;
                timer.Reset(); // schedule next tick
                return false; // keep timer alive
            });
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            // Damage checking is handled by world timers — nothing to do here
        }
    }
}
