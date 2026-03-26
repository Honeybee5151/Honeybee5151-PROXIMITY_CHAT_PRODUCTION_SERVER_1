using System.Linq;
using NLog;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.logic.behaviors;

namespace WorldServer.logic.transitions
{
    /// <summary>
    /// Transitions when an active NPC dialogue exists for this entity (any player is in dialogue with it).
    /// </summary>
    internal class DialogueActiveTransition : Transition
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public DialogueActiveTransition(string targetState)
            : base(targetState)
        {
        }

        protected override bool TickCore(Entity host, TickTime time, ref object state)
        {
            var result = NpcDialogue.ActiveDialogues.Values.Any(npcId => npcId == host.Id);
            if (result)
                Log.Info($"[DialogueActive] Transitioning {host.ObjectDesc?.IdName} (Id={host.Id}) to talking state");
            return result;
        }
    }
}
