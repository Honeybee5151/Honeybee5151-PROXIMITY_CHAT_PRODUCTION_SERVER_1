using System;
using System.Collections.Generic;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;
using Shared.resources;

namespace WorldServer.logic.behaviors.@new.attacks
{
    /// <summary>
    /// Checks HP thresholds every tick. When a 10% threshold is crossed,
    /// spawns a bonus orb that travels top→bottom shooting L/R with Hallucination.
    /// </summary>
    public sealed class DwarfBonusOrb : Behavior
    {
        private readonly float _topY, _bottomY, _centerX;
        private readonly int _touchDamage, _shotDamage;
        private readonly float _orbSpeed;
        private readonly int _effectDuration;

        public DwarfBonusOrb(
            float centerX = 50f, float topY = 5f, float bottomY = 95f,
            int touchDamage = 200, int shotDamage = 70,
            float orbSpeed = 0.1f, int effectDuration = 5000)
        {
            _centerX = centerX;
            _topY = topY;
            _bottomY = bottomY;
            _touchDamage = touchDamage;
            _shotDamage = shotDamage;
            _orbSpeed = orbSpeed;
            _effectDuration = effectDuration;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            state = new HashSet<int>();
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            if (host is not Enemy enemy) return;
            if (state is not HashSet<int> firedThresholds) return;

            var hpPercent = (double)enemy.Health / enemy.MaxHealth;

            for (var threshold = 90; threshold >= 10; threshold -= 10)
            {
                if (hpPercent <= threshold / 100.0 && !firedThresholds.Contains(threshold))
                {
                    firedThresholds.Add(threshold);
                    SpawnBonusOrb(host);
                    break;
                }
            }
        }

        private void SpawnBonusOrb(Entity host)
        {
            var entityId = host.Id;
            var orbX = _centerX;
            var orbY = _topY;
            var targetY = _bottomY;
            var orbSpeed = _orbSpeed;
            var touchDmg = _touchDamage;
            var shotDmg = _shotDamage;
            var effectDur = _effectDuration;
            uint color = 0xFFFFD700;

            var hitCooldowns = new Dictionary<int, int>();
            var shootCooldown = 0;

            void BonusOrbTick(World w, TickTime t)
            {
                var entity = w.GetEntity(entityId);
                if (entity == null) return;

                var dt = t.ElapsedMsDelta / 50f;
                orbY += orbSpeed * dt;

                if (orbY >= targetY) return;

                // Touch damage + Hallucination
                var now = Environment.TickCount;
                w.AOE(new Position(orbX, orbY), 2f, true, p =>
                {
                    if (p is not Player player) return;
                    if (hitCooldowns.TryGetValue(player.Id, out var nextHit) && now < nextHit) return;
                    hitCooldowns[player.Id] = now + 500;
                    (p as IPlayer).Damage(touchDmg, entity);
                    player.ApplyConditionEffect(new ConditionEffect(ConditionEffectIndex.Hallucinating, effectDur));
                    w.BroadcastIfVisible(new AoeMessage(new Position(player.X, player.Y), 1f, touchDmg, ConditionEffectIndex.Hallucinating, effectDur / 1000f, entity.ObjectType, new ARGB(color)), player);
                });

                // Shoot left and right
                shootCooldown -= t.ElapsedMsDelta;
                if (shootCooldown <= 0)
                {
                    shootCooldown = 600;
                    DwarfOrbPair.DamageLineHorizontal(w, entity, orbX, orbY, 10f, shotDmg, color, ConditionEffectIndex.Hallucinating, effectDur);
                    DwarfOrbPair.DamageLineHorizontal(w, entity, orbX, orbY, 90f, shotDmg, color, ConditionEffectIndex.Hallucinating, effectDur);
                }

                w.StartNewTimer(100, BonusOrbTick);
            }

            host.World.StartNewTimer(100, BonusOrbTick);
        }
    }
}
