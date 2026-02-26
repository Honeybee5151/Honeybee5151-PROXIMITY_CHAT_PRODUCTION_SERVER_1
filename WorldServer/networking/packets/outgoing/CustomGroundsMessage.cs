using System.Text;
using Ionic.Zlib;
using Shared;

namespace WorldServer.networking.packets.outgoing
{
    public class CustomGroundsMessage : OutgoingMessage
    {
        public string GroundsXml { get; set; }

        public override MessageId MessageId => MessageId.CUSTOM_GROUNDS;

        public override void Write(NetworkWriter wtr)
        {
            var xml = GroundsXml ?? "";
            var raw = Encoding.UTF8.GetBytes(xml);
            var compressed = ZlibStream.CompressBuffer(raw);
            wtr.Write(compressed.Length);
            wtr.Write(compressed);
        }
    }
}
