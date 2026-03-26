using System;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.logic.transitions
{
    internal class QuestTextActiveTransition : Transition
    {
        private readonly string _questText;
        private bool _logged;

        public QuestTextActiveTransition(string questText, string targetState)
            : base(targetState)
        {
            _questText = questText;
        }

        protected override bool TickCore(Entity host, TickTime time, ref object state)
        {
            var active = host.World?.ActiveQuestText;
            var result = active == _questText;
            if (!_logged)
            {
                Console.WriteLine($"[QuestTextTransition] {host.ObjectDesc?.IdName}: checking '{_questText}' vs ActiveQuestText='{active ?? "null"}' => {result}");
                _logged = true;
            }
            if (result && _logged)
            {
                Console.WriteLine($"[QuestTextTransition] {host.ObjectDesc?.IdName}: TRANSITIONING to idle! ActiveQuestText='{active}'");
            }
            return result;
        }
    }
}
