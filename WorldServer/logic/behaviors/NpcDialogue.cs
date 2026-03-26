using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.logic.behaviors
{
    /// <summary>
    /// When the nearest player comes within range, sends a dialogue GlobalNotification
    /// with the NPC's text and options. Tracks which players have an open dialogue to avoid spam.
    /// </summary>
    internal class NpcDialogue : Behavior
    {
        // Global tracker: accountId -> npcEntityId (so we know who has an open dialogue)
        public static readonly ConcurrentDictionary<int, int> ActiveDialogues = new();

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
            state = 0; // cooldown remaining
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var cd = (int)(state ?? 0);
            cd -= time.ElapsedMsDelta;
            if (cd > 0)
            {
                state = cd;
                return;
            }

            state = _cooldown.Next(Random);

            // Find nearest player within range who doesn't already have an active dialogue
            var player = host.World.PlayersCollision.HitTest(host.X, host.Y, _range)
                .OfType<Player>()
                .Where(p => host.DistTo(p) <= _range && !ActiveDialogues.ContainsKey(p.AccountId))
                .OrderBy(p => host.DistTo(p))
                .FirstOrDefault();

            if (player == null)
                return;

            // Build options JSON array
            var optionsJson = string.Join(",", _options.Select((o, i) => $"{{\"id\":{i},\"label\":\"{EscapeJson(o)}\"}}"));
            var npcName = host.ObjectDesc?.DisplayId ?? host.ObjectDesc?.IdName ?? host.Name;
            var json = $"{{\"npcId\":{host.Id},\"npcName\":\"{EscapeJson(npcName)}\",\"text\":\"{EscapeJson(_text)}\",\"options\":[{optionsJson}]}}";

            var msg = new GlobalNotificationMessage(0, "npcDialogue:" + json);
            player.Client.SendPacket(msg);

            ActiveDialogues[player.AccountId] = host.Id;
        }

        private static string EscapeJson(string s) => s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";
    }
}
