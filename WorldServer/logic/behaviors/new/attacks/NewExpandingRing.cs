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
    /// Expanding ring attack — fires visual on state entry, then TickCore expands the ring
    /// and damages players when the ring edge crosses them.
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

        // State object per-entity
        private class RingState
        {
            public float OriginX;
            public float OriginY;
            public int ElapsedMs;
            public int TotalMs;
            public HashSet<int> HitPlayers = new();
            public bool Done;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            var rs = new RingState
            {
                OriginX = host.X,
                OriginY = host.Y,
                ElapsedMs = 0,
                TotalMs = (int)(ExpandDurationSec * 1000),
                Done = false
            };
            state = rs;

            // Broadcast visual to clients
            host.World.BroadcastIfVisible(new ShowEffect()
            {
                EffectType = EffectType.ExpandingRing,
                TargetObjectId = host.Id,
                Pos1 = new Position() { X = MaxRadius, Y = RingThickness },
                Color = new ARGB(Color),
                Duration = rs.TotalMs
            }, host);
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            if (state is not RingState rs || rs.Done)
                return;

            rs.ElapsedMs += time.ElapsedMsDelta;

            if (rs.ElapsedMs >= rs.TotalMs)
            {
                rs.Done = true;
                return;
            }

            var progress = rs.ElapsedMs / (float)rs.TotalMs;
            var currentRadius = progress * MaxRadius;

            // Ring band = the hitbox area
            var ringInner = Math.Max(0f, currentRadius - RingThickness / 2f);
            var ringOuter = currentRadius + RingThickness / 2f;

            var centerX = rs.OriginX;
            var centerY = rs.OriginY;

            // Check all players within max possible range
            var searchRadius = MaxRadius + RingThickness + 2f;
            var pos = new Position(centerX, centerY);

            host.World.AOE(pos, searchRadius, true, p =>
            {
                if (p is not Player player)
                    return;

                if (rs.HitPlayers.Contains(player.Id))
                    return;

                var dist = player.DistTo(centerX, centerY);

                if (dist >= ringInner && dist <= ringOuter)
                {
                    var hitPos = new Position(player.X, player.Y);
                    host.World.BroadcastIfVisible(new AoeMessage(hitPos, 1f, Damage, Effect, EffectDuration / 1000f, host.ObjectType, new ARGB(Color)), player);

                    (p as IPlayer).Damage(Damage, host);
                    rs.HitPlayers.Add(player.Id);

                    if (Effect != 0 && !player.HasConditionEffect(ConditionEffectIndex.Invincible) && !player.HasConditionEffect(ConditionEffectIndex.Invulnerable))
                        player.ApplyConditionEffect(new ConditionEffect(Effect, EffectDuration));
                }
            });
        }
    }
}
