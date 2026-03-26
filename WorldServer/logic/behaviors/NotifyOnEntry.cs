using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.logic.behaviors
{
    /// <summary>
    /// Sends a GlobalNotification to all players when this state is entered.
    /// </summary>
    internal class NotifyOnEntry : Behavior
    {
        private readonly string _notification;

        public NotifyOnEntry(string notification)
        {
            _notification = notification;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            if (host.World == null)
                return;

            var msg = new GlobalNotificationMessage(0, _notification);
            foreach (var player in host.World.Players.Values)
                player.Client.SendPacket(msg);
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
        }
    }
}
