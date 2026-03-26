using System;
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
        private bool _loggedOnce;

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

            var match = world.QuestText == _questText;
            if (!_loggedOnce)
            {
                Console.WriteLine($"[QuestTextActive] host={host.ObjectDesc?.IdName} world.QuestText='{world.QuestText}' target='{_questText}' match={match}");
                _loggedOnce = true;
            }
            if (match)
                Console.WriteLine($"[QuestTextActive] TRANSITIONING {host.ObjectDesc?.IdName} to idle");
            return match;
        }
    }
}
