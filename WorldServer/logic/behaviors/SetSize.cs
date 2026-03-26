using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.logic.behaviors
{
    /// <summary>
    /// Sets the entity's Size stat on state entry.
    /// </summary>
    internal class SetSize : Behavior
    {
        private readonly int _size;

        public SetSize(int size)
        {
            _size = size;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            host.Size = _size;
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
        }
    }
}
