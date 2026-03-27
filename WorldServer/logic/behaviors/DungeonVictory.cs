using System;
using System.Linq;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.logic.behaviors
{
    /// <summary>
    /// On death, checks if all Quest enemies in the world are dead.
    /// Delays the check so OnDeathSetQuestText can fire first — if quest text
    /// changed (new stage started), victory is suppressed automatically.
    /// </summary>
    internal class DungeonVictory : Behavior
    {
        private readonly string _questText;

        public DungeonVictory(string questText = null)
        {
            _questText = questText;
        }

        public override void OnDeath(Entity host, ref TickTime time)
        {
            var world = host.World;
            if (world == null)
                return;

            // Snapshot current quest text before OnDeathSetQuestText runs
            var questTextBefore = world.QuestText ?? "";

            Console.WriteLine($"[DungeonVictory] {host.ObjectDesc?.IdName} died. QuestText='{questTextBefore}'. Scheduling delayed check...");

            // Delay check so OnDeathSetQuestText and other OnDeath behaviors finish first
            world.StartNewTimer(500, (w, t) =>
            {
                var questTextAfter = w.QuestText ?? "";

                // If quest text changed, a new stage started (e.g., "Talk to Boss Giant") — skip victory
                if (questTextAfter != questTextBefore)
                {
                    Console.WriteLine($"[DungeonVictory] QuestText changed from '{questTextBefore}' to '{questTextAfter}' — new stage, skipping victory.");
                    return;
                }

                // Check if any Quest enemies remain
                var remaining = w.Quests.Values
                    .Where(e => !e.Dead)
                    .ToList();

                Console.WriteLine($"[DungeonVictory] Remaining quest enemies: {remaining.Count} ({string.Join(", ", remaining.Select(e => e.ObjectDesc?.IdName ?? "?"))})");

                if (remaining.Count > 0)
                    return;

                // All clear — broadcast victory
                Console.WriteLine("[DungeonVictory] All quest enemies dead, no new stages — broadcasting victory!");
                var victoryMsg = new GlobalNotificationMessage(0, "dungeonVictory");
                foreach (var player in w.Players.Values)
                    player.Client.SendPacket(victoryMsg);
            });
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
        }
    }
}
