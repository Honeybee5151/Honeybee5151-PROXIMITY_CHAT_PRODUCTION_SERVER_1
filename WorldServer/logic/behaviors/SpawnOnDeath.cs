using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.logic.behaviors
{
    /// <summary>
    /// Spawns a static entity at the host's death position.
    /// Used for ground imprints, debris, etc.
    /// </summary>
    internal class SpawnOnDeath : Behavior
    {
        private readonly ushort _target;

        public SpawnOnDeath(string target)
        {
            _target = GetObjType(target);
        }

        public override void OnDeath(Entity host, ref TickTime time)
        {
            var owner = host.World;
            var entity = Entity.Resolve(host.GameServer, _target);
            if (entity == null)
                return;

            entity.Move(host.X, host.Y);
            owner.EnterWorld(entity);
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
        }
    }
}
