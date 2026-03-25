using Shared;
using WorldServer.core.worlds;
using WorldServer.networking;

namespace WorldServer.core.net.handlers
{
    public class CrouchHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.CROUCH;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime tickTime)
        {
            var isCrouching = rdr.ReadBoolean();

            var player = client.Player;
            if (player == null || player.World == null)
                return;

            player.IsCrouching = isCrouching ? 1 : 0;
        }
    }
}
