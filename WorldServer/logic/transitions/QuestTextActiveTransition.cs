using System.Linq;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.logic.transitions
{
    /// <summary>
    /// Transitions when all specified quest enemies in the world are dead.
    /// Uses world.Quests dictionary (same as DungeonVictory) for reliable detection.
    /// Includes a startup guard to avoid false positives before entities are loaded.
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

            // Wait until we've seen the named enemies at least once before allowing transition.
            // This prevents false positives during world startup before entities are loaded.
            if (state == null)
            {
                // Check if any of the named enemies exist yet
                var anyExist = world.Quests.Values
                    .Any(e => _entityNames.Contains(e.ObjectDesc?.IdName));
                if (!anyExist)
                {
                    // Also check Enemies collection as fallback
                    anyExist = world.Enemies.Values
                        .Any(e => _entityNames.Contains(e.ObjectDesc?.IdName));
                }

                if (anyExist)
                    state = true; // Mark that we've seen them
                else
                    return false; // Haven't seen them yet, don't transition
            }

            // Now check if ALL named entities are dead or removed
            foreach (var name in _entityNames)
            {
                var alive = world.Quests.Values
                    .Any(e => e.ObjectDesc?.IdName == name && !e.Dead);
                if (!alive)
                {
                    // Double-check in Enemies collection
                    alive = world.Enemies.Values
                        .Any(e => e.ObjectDesc?.IdName == name && !e.Dead);
                }
                if (alive)
                    return false;
            }

            return true;
        }
    }
}
