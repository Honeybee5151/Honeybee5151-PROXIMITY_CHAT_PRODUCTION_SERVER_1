using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NLog;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.logic.behaviors
{
    /// <summary>
    /// When the nearest player comes within range, sends a dialogue GlobalNotification
    /// with the NPC's text and options. Tracks which players have an open dialogue to avoid spam.
    /// Players who dismiss dialogue must leave range and return before it triggers again.
    /// </summary>
    internal class NpcDialogue : Behavior
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        // Global tracker: accountId -> npcEntityId (player has dialogue open)
        public static readonly ConcurrentDictionary<int, int> ActiveDialogues = new();

        // Global tracker: accountId -> npcEntityId (player dismissed, must leave range first)
        public static readonly ConcurrentDictionary<int, int> DismissedDialogues = new();

        private readonly string _text;
        private readonly string[] _options;
        private readonly float _range;
        private Cooldown _cooldown;

        public NpcDialogue(string text, string[] options, float range = 2f, Cooldown cooldown = default)
        {
            _text = text;
            _options = options;
            _range = range;
            _cooldown = cooldown.Normalize(500);
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            state = 0;
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            if (host.World == null)
                return;

            var cd = state is int c ? c : 0;
            cd -= time.ElapsedMsDelta;
            if (cd > 0)
            {
                state = cd;
                return;
            }

            state = _cooldown.Next(Random);

            // Clear dismissed players who have left range
            foreach (var kvp in DismissedDialogues)
            {
                if (kvp.Value != host.Id)
                    continue;

                var dismissedPlayer = host.World.Players.Values.FirstOrDefault(p => p.AccountId == kvp.Key);
                if (dismissedPlayer == null || host.DistTo(dismissedPlayer) > _range)
                    DismissedDialogues.TryRemove(kvp.Key, out _);
            }

            // Find nearest player within range who doesn't have active or dismissed dialogue with THIS NPC
            var player = host.World.PlayersCollision.HitTest(host.X, host.Y, _range)
                .OfType<Player>()
                .Where(p => host.DistTo(p) <= _range
                    && !(ActiveDialogues.TryGetValue(p.AccountId, out var activeNpc) && activeNpc == host.Id)
                    && !(DismissedDialogues.TryGetValue(p.AccountId, out var dismissedNpc) && dismissedNpc == host.Id))
                .OrderBy(p => host.DistTo(p))
                .FirstOrDefault();

            if (player == null)
                return;

            // Build options JSON array
            var optionsJson = string.Join(",", _options.Select((o, i) => $"{{\"id\":{i},\"label\":\"{EscapeJson(o)}\"}}"));
            var npcName = host.ObjectDesc?.DisplayId ?? host.ObjectDesc?.IdName ?? "NPC";
            var json = $"{{\"npcId\":{host.Id},\"npcName\":\"{EscapeJson(npcName)}\",\"text\":\"{EscapeJson(_text)}\",\"options\":[{optionsJson}]}}";

            var msg = new GlobalNotificationMessage(0, "npcDialogue:" + json);
            player.Client.SendPacket(msg);

            Log.Info($"[NpcDialogue] Sent dialogue to {player.Name} from {npcName}");
            ActiveDialogues[player.AccountId] = host.Id;
        }

        private static string EscapeJson(string s) => s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";
    }
}
