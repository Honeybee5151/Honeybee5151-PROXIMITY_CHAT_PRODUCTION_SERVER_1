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
    /// A virtual orb that bounces off room walls and shoots toward center.
    /// Checks for entity label "bouncing_orb_active" to know when to stop.
    /// </summary>
    public sealed class DwarfBouncingOrb : Behavior
    {
        private readonly float _minX, _maxX, _minY, _maxY, _centerX;
        private readonly int _touchDamage, _shotDamage;
        private readonly float _velX, _velY;
        private readonly uint _color;

        public DwarfBouncingOrb(
            float minX = 20f, float maxX = 80f,
            float minY = 10f, float maxY = 90f,
            float centerX = 50f,
            int touchDamage = 200, int shotDamage = 70,
            float velX = 0.12f, float velY = 0.08f,
            uint color = 0xFFFF4400)
        {
            _minX = minX;
            _maxX = maxX;
            _minY = minY;
            _maxY = maxY;
            _centerX = centerX;
            _touchDamage = touchDamage;
            _shotDamage = shotDamage;
            _velX = velX;
            _velY = velY;
            _color = color;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            state = null;
            host.SetLabel("bouncing_orb_active");

            var entityId = host.Id;
            var orbX = host.X;
            var orbY = host.Y;
            var velX = _velX;
            var velY = _velY;

            var minX = _minX;
            var maxX = _maxX;
            var minY = _minY;
            var maxY = _maxY;
            var centerX = _centerX;
            var touchDmg = _touchDamage;
            var shotDmg = _shotDamage;
            var color = _color;

            var hitCooldowns = new Dictionary<int, int>();
            var shootCooldown = 0;
            var visualCooldown = 0;

            void BounceTick(World w, TickTime t)
            {
                var entity = w.GetEntity(entityId);
                if (entity == null) return;
                if (!entity.HasLabel("bouncing_orb_active")) return;

                var dt = t.ElapsedMsDelta / 50f;
                orbX += velX * dt;
                orbY += velY * dt;

                if (orbX <= minX) { orbX = minX; velX = Math.Abs(velX); }
                if (orbX >= maxX) { orbX = maxX; velX = -Math.Abs(velX); }
                if (orbY <= minY) { orbY = minY; velY = Math.Abs(velY); }
                if (orbY >= maxY) { orbY = maxY; velY = -Math.Abs(velY); }

                // Visual (every 500ms)
                visualCooldown -= t.ElapsedMsDelta;
                if (visualCooldown <= 0)
                {
                    visualCooldown = 500;
                    var visPos = new Position(orbX, orbY);
                    w.BroadcastIfVisible(new ShowEffect()
                    {
                        EffectType = EffectType.AreaBlast,
                        TargetObjectId = entity.Id,
                        Pos1 = new Position() { X = 1.5f },
                        Color = new ARGB(color),
                        Duration = 500
                    }, ref visPos);
                }

                // Touch damage
                var now = Environment.TickCount;
                w.AOE(new Position(orbX, orbY), 1.5f, true, p =>
                {
                    if (p is not Player player) return;
                    if (hitCooldowns.TryGetValue(player.Id, out var nextHit) && now < nextHit) return;
                    hitCooldowns[player.Id] = now + 500;
                    (p as IPlayer).Damage(touchDmg, entity);
                    w.BroadcastIfVisible(new AoeMessage(new Position(player.X, player.Y), 1f, touchDmg, 0, 0, entity.ObjectType, new ARGB(color)), player);
                });

                // Shoot toward center
                shootCooldown -= t.ElapsedMsDelta;
                if (shootCooldown <= 0)
                {
                    shootCooldown = 1000;
                    DwarfOrbPair.DamageLineHorizontal(w, entity, orbX, orbY, centerX, shotDmg, color);
                }

                w.StartNewTimer(100, BounceTick);
            }

            host.World.StartNewTimer(100, BounceTick);
        }

        protected override void TickCore(Entity host, TickTime time, ref object state) { }
    }
}
