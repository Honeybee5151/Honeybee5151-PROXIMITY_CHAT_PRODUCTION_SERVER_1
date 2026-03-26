using System;
using System.Linq;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.logic.transitions
{
    /// <summary>
    /// Transitions when all specified quest enemies in the world are dead.
    /// Unlike EntitiesNotExistsTransition, this checks the World.Quests dictionary
    /// directly (not collision maps), so it works regardless of distance.
    /// </summary>
    internal class QuestTextActiveTransition : Transition
    {
        private readonly string[] _entityNames;

        public QuestTextActiveTransition(string targetState, params string[] entityNames)
            : base(targetState)
        {
            _entityNames = entityNames;
        }

        protected override bool TickCore(Entity host, TickTime time, ref object state)
        {
            var world = host.World;
            if (world == null)
                return false;

            // Check if ALL named entities are dead or removed from the world
            foreach (var name in _entityNames)
            {
                var alive = world.Enemies.Values
                    .Any(e => e.ObjectDesc?.IdName == name && !e.Dead);
                if (alive)
                    return false;
            }

            return true;
        }
    }
}
