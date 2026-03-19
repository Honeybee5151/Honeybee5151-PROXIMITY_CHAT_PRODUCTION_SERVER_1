using Shared;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.net.stats;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.net.handlers
{
    public class DashHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.DASH;

        private const int DASH_DURATION_MS = 400;
        private const int DASH_COOLDOWN_MS = 5000;
        private const int SPEED_BOOST = 100;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime tickTime)
        {
            var time = rdr.ReadInt32();

            var player = client.Player;
            if (player == null || player.World == null)
                return;

            if (player.DashCooldown > 0)
                return;

            // Apply invulnerable for dash duration
            player.ApplyConditionEffect(ConditionEffectIndex.Invulnerable, DASH_DURATION_MS);

            // Apply speed boost
            var speedIdx = StatsManager.GetStatIndex(StatDataType.Speed);
            player.Stats.Boost.ActivateBoost[speedIdx].Push(SPEED_BOOST, false);
            player.Stats.ReCalculateValues();

            // Timer to remove speed boost
            player.World.StartNewTimer(DASH_DURATION_MS, (world, t) =>
            {
                player.Stats.Boost.ActivateBoost[speedIdx].Pop(SPEED_BOOST, false);
                player.Stats.ReCalculateValues();
            });

            // Set cooldown
            player.DashCooldown = DASH_COOLDOWN_MS;

            // Visual feedback
            player.World.BroadcastIfVisible(new ShowEffect()
            {
                EffectType = EffectType.Potion,
                TargetObjectId = player.Id,
                Color = new ARGB(0xff00aaff)
            }, player);
        }
    }
}
