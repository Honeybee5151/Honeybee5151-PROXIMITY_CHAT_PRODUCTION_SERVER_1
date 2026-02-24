using Shared;

namespace WorldServer.networking.packets.outgoing
{
    public class CustomDungeonAssetsMessage : OutgoingMessage
    {
        public string AssetsXml { get; set; }

        public override MessageId MessageId => MessageId.CUSTOM_DUNGEON_ASSETS;

        public override void Write(NetworkWriter wtr)
        {
            wtr.WriteUTF32(AssetsXml ?? "");
        }
    }
}
