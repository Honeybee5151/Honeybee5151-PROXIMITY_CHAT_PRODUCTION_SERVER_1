using Shared;

namespace WorldServer.networking.packets.outgoing
{
    public class CustomGroundsMessage : OutgoingMessage
    {
        public string GroundsXml { get; set; }

        public override MessageId MessageId => MessageId.CUSTOM_GROUNDS;

        public override void Write(NetworkWriter wtr)
        {
            wtr.WriteUTF32(GroundsXml ?? "");
        }
    }
}
