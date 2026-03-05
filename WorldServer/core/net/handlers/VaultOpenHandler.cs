using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Shared;
using Shared.database.character.inventory;
using Shared.database.vault;
using WorldServer.core.worlds;
using WorldServer.networking;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.net.handlers
{
    public sealed class VaultOpenHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.VAULT_OPEN;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime tickTime)
        {
            var sectionIndex = rdr.ReadByte();
            var player = client.Player;

            if (player == null || client.Account == null)
                return;

            // Run migration if needed
            if (!client.Account.VaultMigrated)
                MigrateOldVault(client);

            if (sectionIndex == 0xFF)
            {
                // Send all 10 sections
                for (var i = 0; i < 10; i++)
                    SendSection(client, i);
            }
            else if (sectionIndex < 10)
            {
                SendSection(client, sectionIndex);
            }
        }

        private void SendSection(Client client, int sectionIndex)
        {
            var section = new DbVaultSection(client.Account, sectionIndex);
            var items = section.Items;
            var datas = section.ItemDatas;

            var entries = new List<VaultSlotEntry>();
            for (var i = 0; i < DbVaultSection.SLOTS_PER_SECTION; i++)
            {
                if (items[i] == 0xFFFF)
                    continue;

                var dataJson = "";
                if (datas != null && i < datas.Length && datas[i] != null)
                    dataJson = datas[i].GetData();

                entries.Add(new VaultSlotEntry
                {
                    SlotIndex = (short)i,
                    ItemType = items[i],
                    ItemDataJson = dataJson
                });
            }

            client.SendPacket(new VaultDataMessage
            {
                SectionIndex = (byte)sectionIndex,
                Entries = entries
            });
        }

        private void MigrateOldVault(Client client)
        {
            var account = client.Account;
            var vaultCount = account.VaultCount;

            if (vaultCount <= 0)
            {
                account.VaultMigrated = true;
                account.FlushAsync();
                return;
            }

            // Collect all items from old vault chests into Misc section (index 9)
            var allItems = new List<(ushort type, ItemData data)>();

            for (var i = 0; i < vaultCount; i++)
            {
                var oldChest = new DbVaultSingle(account, i);
                var items = oldChest.Items;
                var datas = oldChest.ItemDatas;

                for (var j = 0; j < items.Length; j++)
                {
                    if (items[j] != 0xFFFF)
                    {
                        var data = (datas != null && j < datas.Length) ? datas[j] : null;
                        allItems.Add((items[j], data));
                    }
                }
            }

            if (allItems.Count > 0)
            {
                var miscSection = new DbVaultSection(account, 9); // Misc = section 9
                var sectionItems = miscSection.Items;
                var sectionDatas = miscSection.ItemDatas;

                var slotIndex = 0;
                foreach (var (type, data) in allItems)
                {
                    if (slotIndex >= DbVaultSection.SLOTS_PER_SECTION)
                        break;

                    sectionItems[slotIndex] = type;
                    if (data != null)
                        sectionDatas[slotIndex] = data;
                    slotIndex++;
                }

                miscSection.Items = sectionItems;
                miscSection.ItemDatas = sectionDatas;
                miscSection.FlushAsync();
            }

            account.VaultMigrated = true;
            account.FlushAsync();
        }
    }
}
