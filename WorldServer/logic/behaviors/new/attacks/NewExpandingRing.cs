using System;
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
    /// Expanding ring attack — a shockwave ring expands outward from the mob.
    /// Players on the ring edge take damage. Invulnerable (e.g. dashing) players are immune.
    /// The ring is purely server-authoritative: damage is checked each tick as the ring grows.
    /// A ShowEffect is broadcast for the client-side visual.
    /// </summary>
    public sealed class NewExpandingRing : Behavior
    {
        private readonly float MaxRadius;
        private readonly float ExpandDurationSec;
        private readonly float RingThickness;
        private readonly int Damage;
        private readonly float Cooldown;
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
            Cooldown = cooldown;
            Color = color;
            Effect = effect;
            EffectDuration = effectDuration;
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var s = state as RingState;
            if (s == null)
            {
                s = new RingState { CooldownMs = (int)(Cooldown * 1000) };
                state = s;
            }

            // Ring is expanding — check damage each tick
            if (s.Expanding)
            {
                s.ElapsedMs += time.ElapsedMsDelta;
                var totalMs = (int)(ExpandDurationSec * 1000);

                if (s.ElapsedMs >= totalMs)
                {
                    s.Expanding = false;
                    s.CooldownMs = (int)(Cooldown * 1000);
                    return;
                }

                var progress = s.ElapsedMs / (float)totalMs;
                var currentRadius = progress * MaxRadius;
                var innerEdge = currentRadius - RingThickness / 2f;
                var outerEdge = currentRadius + RingThickness / 2f;

                // Query all players within outer edge
                host.World.AOE(new Position(host.X, host.Y), outerEdge, true, p =>
                {
                    if (p is not Player player)
                        return;

                    // Skip players already hit by this ring
                    if (s.HitPlayers.Contains(player.Id))
                        return;

                    var dist = player.DistTo(host);
                    if (dist >= innerEdge && dist <= outerEdge)
                    {
                        player.Damage(Damage, host);
                        s.HitPlayers.Add(player.Id);

                        if (Effect != 0 && !player.HasConditionEffect(ConditionEffectIndex.Invincible) && !player.HasConditionEffect(ConditionEffectIndex.Invulnerable))
                            player.ApplyConditionEffect(new ConditionEffect(Effect, EffectDuration));
                    }
                });

                return;
            }

            // Cooldown
            s.CooldownMs -= time.ElapsedMsDelta;
            if (s.CooldownMs > 0)
                return;

            if (host.HasConditionEffect(ConditionEffectIndex.Stunned))
                return;

            // Start new ring
            s.Expanding = true;
            s.ElapsedMs = 0;
            s.HitPlayers.Clear();

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

        class RingState
        {
            public bool Expanding;
            public int ElapsedMs;
            public int CooldownMs;
            public System.Collections.Generic.HashSet<int> HitPlayers = new();
        }
    }
}
