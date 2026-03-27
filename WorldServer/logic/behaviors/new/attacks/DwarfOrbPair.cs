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
    public sealed class DwarfOrbPair : Behavior
    {
        private readonly float _leftX, _rightX, _topY, _bottomY, _centerX;
        private readonly int _touchDamage, _shotDamage;
        private readonly float _orbSpeed;
        private readonly uint _color;

        public DwarfOrbPair(
            float leftX = 30f, float rightX = 70f,
            float topY = 10f, float bottomY = 90f,
            float centerX = 50f,
            int touchDamage = 200, int shotDamage = 70,
            float orbSpeed = 0.15f,
            uint color = 0xFF4488FF)
        {
            _leftX = leftX;
            _rightX = rightX;
            _topY = topY;
            _bottomY = bottomY;
            _centerX = centerX;
            _touchDamage = touchDamage;
            _shotDamage = shotDamage;
            _orbSpeed = orbSpeed;
            _color = color;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            state = null;
            host.SetLabel("orbs_active");

            var entityId = host.Id;
            var leftOrbY = _topY;
            var rightOrbY = _bottomY;
            var leftDir = 1f;
            var rightDir = -1f;
            var shootCooldown = 0;

            var leftX = _leftX;
            var rightX = _rightX;
            var topY = _topY;
            var bottomY = _bottomY;
            var centerX = _centerX;
            var touchDmg = _touchDamage;
            var shotDmg = _shotDamage;
            var orbSpeed = _orbSpeed;
            var color = _color;

            var hitCooldowns = new Dictionary<int, int>();

            void OrbTick(World w, TickTime t)
            {
                var entity = w.GetEntity(entityId);
                if (entity == null) return;
                if (!entity.HasLabel("orbs_active")) return;

                var dt = t.ElapsedMsDelta / 50f;
                leftOrbY += leftDir * orbSpeed * dt;
                rightOrbY += rightDir * orbSpeed * dt;

                if (leftOrbY >= bottomY) { leftOrbY = bottomY; leftDir = -1f; }
                if (leftOrbY <= topY) { leftOrbY = topY; leftDir = 1f; }
                if (rightOrbY >= bottomY) { rightOrbY = bottomY; rightDir = -1f; }
                if (rightOrbY <= topY) { rightOrbY = topY; rightDir = 1f; }

                // Visual
                BroadcastOrbVisual(w, entity, leftX, leftOrbY, color);
                BroadcastOrbVisual(w, entity, rightX, rightOrbY, color);

                // Touch damage
                var now = Environment.TickCount;
                CheckTouchDamage(w, entity, leftX, leftOrbY, touchDmg, color, hitCooldowns, now);
                CheckTouchDamage(w, entity, rightX, rightOrbY, touchDmg, color, hitCooldowns, now);

                // Horizontal shots toward center
                shootCooldown -= t.ElapsedMsDelta;
                if (shootCooldown <= 0)
                {
                    shootCooldown = 800;
                    DamageLineHorizontal(w, entity, leftX, leftOrbY, centerX, shotDmg, color);
                    DamageLineHorizontal(w, entity, rightX, rightOrbY, centerX, shotDmg, color);
                }

                w.StartNewTimer(100, OrbTick);
            }

            host.World.StartNewTimer(100, OrbTick);
        }

        protected override void TickCore(Entity host, TickTime time, ref object state) { }

        private static void BroadcastOrbVisual(World w, Entity entity, float x, float y, uint color)
        {
            var pos = new Position(x, y);
            w.BroadcastIfVisible(new ShowEffect()
            {
                EffectType = EffectType.AreaBlast,
                TargetObjectId = entity.Id,
                Pos1 = new Position() { X = 1.5f },
                Color = new ARGB(color),
                Duration = 150
            }, ref pos);
        }

        private static void CheckTouchDamage(World w, Entity entity, float x, float y, int damage, uint color, Dictionary<int, int> hitCooldowns, int now)
        {
            w.AOE(new Position(x, y), 1.5f, true, p =>
            {
                if (p is not Player player) return;
                if (hitCooldowns.TryGetValue(player.Id, out var nextHit) && now < nextHit) return;
                hitCooldowns[player.Id] = now + 500;
                (p as IPlayer).Damage(damage, entity);
                w.BroadcastIfVisible(new AoeMessage(new Position(player.X, player.Y), 1f, damage, 0, 0, entity.ObjectType, new ARGB(color)), player);
            });
        }

        internal static void DamageLineHorizontal(World w, Entity source, float fromX, float fromY, float towardX, int damage, uint color, ConditionEffectIndex effect = 0, int effectDuration = 0)
        {
            var fromPos = new Position(fromX, fromY);
            w.BroadcastIfVisible(new ShowEffect()
            {
                EffectType = EffectType.Stream,
                TargetObjectId = source.Id,
                Pos1 = new Position() { X = towardX, Y = fromY },
                Color = new ARGB(color),
                Duration = 400
            }, ref fromPos);

            var minX = Math.Min(fromX, towardX);
            var maxX = Math.Max(fromX, towardX);
            var searchRadius = (maxX - minX) / 2f + 2f;
            var centerX = (fromX + towardX) / 2f;

            w.AOE(new Position(centerX, fromY), searchRadius, true, p =>
            {
                if (p is not Player player) return;
                if (Math.Abs(player.Y - fromY) > 1.5f) return;
                if (player.X < minX - 1f || player.X > maxX + 1f) return;

                (p as IPlayer).Damage(damage, source);
                w.BroadcastIfVisible(new AoeMessage(new Position(player.X, player.Y), 1f, damage, effect, effectDuration / 1000f, source.ObjectType, new ARGB(color)), player);

                if (effect != 0 && !player.HasConditionEffect(ConditionEffectIndex.Invincible) && !player.HasConditionEffect(ConditionEffectIndex.Invulnerable))
                    player.ApplyConditionEffect(new ConditionEffect(effect, effectDuration));
            });
        }
    }
}
