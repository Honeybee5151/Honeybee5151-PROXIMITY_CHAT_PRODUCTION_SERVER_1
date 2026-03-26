using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.logic.transitions
{
    internal class QuestTextActiveTransition : Transition
    {
        private readonly string _questText;

        public QuestTextActiveTransition(string questText, string targetState)
            : base(targetState)
        {
            _questText = questText;
        }

        protected override bool TickCore(Entity host, TickTime time, ref object state)
        {
            return host.World?.ActiveQuestText == _questText;
        }
    }
}
