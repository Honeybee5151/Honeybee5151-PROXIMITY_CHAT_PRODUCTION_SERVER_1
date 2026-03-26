using System.Linq;
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
        public DialogueActiveTransition(string targetState)
            : base(targetState)
        {
        }

        protected override bool TickCore(Entity host, TickTime time, ref object state)
        {
            return NpcDialogue.ActiveDialogues.Values.Any(npcId => npcId == host.Id);
        }
    }
}
