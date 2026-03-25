using System;
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
    /// Each state entry fires exactly one ring. No repeat/cooldown system.
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
            // Fire the ring immediately on state entry
            var s = new RingState
            {
                Expanding = true,
                ElapsedMs = 0,
                Fired = true
            };
            state = s;

            // Broadcast visual to clients
            host.World.BroadcastIfVisible(new ShowEffect()
            {
                EffectType = EffectType.ExpandingRing,
                TargetObjectId = host.Id,
                Pos1 = new Position() { X = MaxRadius, Y = RingThickness },
                Color = new ARGB(Color),
                Duration = (int)(ExpandDurationSec * 1000)
            }, host);
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var s = state as RingState;
            if (s == null || !s.Expanding)
                return;

            s.ElapsedMs += time.ElapsedMsDelta;
            var totalMs = (int)(ExpandDurationSec * 1000);

            if (s.ElapsedMs >= totalMs)
            {
                s.Expanding = false;
                return;
            }

            var progress = s.ElapsedMs / (float)totalMs;
            var currentRadius = progress * MaxRadius;
            var innerEdge = currentRadius - RingThickness / 2f;
            var outerEdge = currentRadius + RingThickness / 2f;

            // Query all players near the ring
            var pos = new Position(host.X, host.Y);
            var searchRadius = outerEdge + 2f;
            host.World.AOE(pos, searchRadius, true, p =>
            {
                if (p is not Player player)
                    return;

                var dist = player.DistTo(host.X, host.Y);
                // Skip players already hit by this ring
                if (s.HitPlayers.Contains(player.Id))
                    return;

                if (dist >= innerEdge && dist <= outerEdge)
                {
                    // Send AoE message so client shows damage popup
                    var hitPos = new Position(player.X, player.Y);
                    host.World.BroadcastIfVisible(new AoeMessage(hitPos, 1f, Damage, Effect, EffectDuration / 1000f, host.ObjectType, new ARGB(Color)), player);

                    (p as IPlayer).Damage(Damage, host);
                    s.HitPlayers.Add(player.Id);

                    if (Effect != 0 && !player.HasConditionEffect(ConditionEffectIndex.Invincible) && !player.HasConditionEffect(ConditionEffectIndex.Invulnerable))
                        player.ApplyConditionEffect(new ConditionEffect(Effect, EffectDuration));
                }
            });
        }

        class RingState
        {
            public bool Expanding;
            public bool Fired;
            public int ElapsedMs;
            public System.Collections.Generic.HashSet<int> HitPlayers = new();
        }
    }
}
