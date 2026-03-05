using System.Collections.Generic;
using Shared;

namespace WorldServer.networking.packets.outgoing
{
    public class VaultDataMessage : OutgoingMessage
    {
        public override MessageId MessageId => MessageId.VAULT_DATA;

        public byte SectionIndex { get; set; }
        public List<VaultSlotEntry> Entries { get; set; } = new();

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write(SectionIndex);
            wtr.Write((short)Entries.Count);
            foreach (var entry in Entries)
            {
                wtr.Write(entry.SlotIndex);
                wtr.Write(entry.ItemType);
                wtr.WriteUTF16(entry.ItemDataJson ?? "");
            }
        }
    }

    public struct VaultSlotEntry
    {
        public short SlotIndex;
        public int ItemType;
        public string ItemDataJson;
    }
}
