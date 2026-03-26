using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.logic.transitions
{
    /// <summary>
    /// Transitions when the world's QuestText matches the specified text.
    /// Ties directly to the quest objective shown in the client's top-left UI.
    /// </summary>
    internal class QuestTextActiveTransition : Transition
    {
        private readonly string _questText;

        public QuestTextActiveTransition(string targetState, string questText)
            : base(targetState)
        {
            _questText = questText;
        }

        protected override bool TickCore(Entity host, TickTime time, ref object state)
        {
            var world = host.World;
            if (world == null)
                return false;

            return world.QuestText == _questText;
        }
    }
}
