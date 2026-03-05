using Shared;
using Shared.database.character.inventory;
using Shared.database.vault;
using WorldServer.core.worlds;
using WorldServer.networking;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.net.handlers
{
    public sealed class VaultSwapHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.VAULT_SWAP;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime tickTime)
        {
            var action = rdr.ReadByte();          // 0=inv→vault, 1=vault→inv, 2=vault→vault
            var sectionIndex = rdr.ReadByte();     // vault section 0-9
            var vaultSlotIndex = rdr.ReadInt16();   // vault slot 0-399
            var vaultItemType = rdr.ReadInt32();    // expected item type at vault slot
            var invSlotIndex = rdr.ReadByte();      // player inv slot 0-19
            var invItemType = rdr.ReadInt32();      // expected item type at inv slot
            var destSectionIndex = rdr.ReadByte();  // destination section (vault→vault only)
            var destVaultSlotIndex = rdr.ReadInt16(); // destination slot (vault→vault only)

            var player = client.Player;
            if (player == null || client.Account == null)
                return;

            // Validate section indices
            if (sectionIndex >= 10 || vaultSlotIndex < 0 || vaultSlotIndex >= DbVaultSection.SLOTS_PER_SECTION)
            {
                client.SendPacket(new InvResult { Result = 1 });
                return;
            }

            switch (action)
            {
                case 0: // inventory → vault
                    HandleInvToVault(client, player, sectionIndex, vaultSlotIndex, vaultItemType, invSlotIndex, invItemType);
                    break;
                case 1: // vault → inventory
                    HandleVaultToInv(client, player, sectionIndex, vaultSlotIndex, vaultItemType, invSlotIndex, invItemType);
                    break;
                case 2: // vault → vault
                    HandleVaultToVault(client, sectionIndex, vaultSlotIndex, vaultItemType, destSectionIndex, destVaultSlotIndex);
                    break;
                default:
                    client.SendPacket(new InvResult { Result = 1 });
                    break;
            }
        }

        private void HandleInvToVault(Client client, objects.Player player, int sectionIndex, int vaultSlot, int expectedVaultType, int invSlot, int expectedInvType)
        {
            if (invSlot >= player.Inventory.Length)
            {
                client.SendPacket(new InvResult { Result = 1 });
                return;
            }

            var section = new DbVaultSection(client.Account, sectionIndex);
            var vaultItems = section.Items;
            var vaultDatas = section.ItemDatas;

            // Validate current state matches expectations (anti-cheat)
            var currentVaultType = vaultItems[vaultSlot];
            var currentInvItem = player.Inventory[invSlot];
            var currentInvType = currentInvItem?.ObjectType ?? 0xFFFF;

            if (currentVaultType != (ushort)expectedVaultType || currentInvType != (ushort)expectedInvType)
            {
                client.SendPacket(new InvResult { Result = 1 });
                return;
            }

            // Save originals before modifying
            var invData = player.Inventory.Data[invSlot];
            var originalVaultData = vaultDatas != null && vaultSlot < vaultDatas.Length ? vaultDatas[vaultSlot] : null;

            // Update player inventory first
            var trans = player.Inventory.CreateTransaction();
            var dataTrans = player.Inventory.CreateDataTransaction();

            if (currentVaultType != 0xFFFF)
            {
                // Vault had an item — put it in player inv
                var vaultItem = player.GameServer.Resources.GameData.Items.ContainsKey((ushort)currentVaultType)
                    ? player.GameServer.Resources.GameData.Items[(ushort)currentVaultType]
                    : null;
                trans[invSlot] = vaultItem;
                dataTrans[invSlot] = originalVaultData;
            }
            else
            {
                // Vault was empty — just remove from inv
                trans[invSlot] = null;
                dataTrans[invSlot] = null;
            }

            // Update vault: put inv item in vault slot
            vaultItems[vaultSlot] = (ushort)currentInvType;
            vaultDatas[vaultSlot] = currentInvType != 0xFFFF ? invData : null;

            if (!objects.inventory.Inventory.Execute(trans))
            {
                client.SendPacket(new InvResult { Result = 1 });
                return;
            }
            objects.inventory.Inventory.DatExecute(dataTrans);

            // Save vault to Redis
            section.Items = vaultItems;
            section.ItemDatas = vaultDatas;
            section.FlushAsync();

            client.SendPacket(new InvResult { Result = 0 });
        }

        private void HandleVaultToInv(Client client, objects.Player player, int sectionIndex, int vaultSlot, int expectedVaultType, int invSlot, int expectedInvType)
        {
            if (invSlot >= player.Inventory.Length)
            {
                client.SendPacket(new InvResult { Result = 1 });
                return;
            }

            var section = new DbVaultSection(client.Account, sectionIndex);
            var vaultItems = section.Items;
            var vaultDatas = section.ItemDatas;

            var currentVaultType = vaultItems[vaultSlot];
            var currentInvItem = player.Inventory[invSlot];
            var currentInvType = currentInvItem?.ObjectType ?? 0xFFFF;

            if (currentVaultType != (ushort)expectedVaultType || currentInvType != (ushort)expectedInvType)
            {
                client.SendPacket(new InvResult { Result = 1 });
                return;
            }

            var vaultData = vaultDatas != null && vaultSlot < vaultDatas.Length ? vaultDatas[vaultSlot] : null;
            var invData = player.Inventory.Data[invSlot];

            // Update vault: put inv item in vault slot
            vaultItems[vaultSlot] = (ushort)currentInvType;
            if (currentInvType != 0xFFFF)
                vaultDatas[vaultSlot] = invData;
            else
                vaultDatas[vaultSlot] = null;

            // Update player: put vault item in inv slot
            var trans = player.Inventory.CreateTransaction();
            var dataTrans = player.Inventory.CreateDataTransaction();

            if (currentVaultType != 0xFFFF)
            {
                var vaultItem = player.GameServer.Resources.GameData.Items.ContainsKey((ushort)currentVaultType)
                    ? player.GameServer.Resources.GameData.Items[(ushort)currentVaultType]
                    : null;
                trans[invSlot] = vaultItem;
                dataTrans[invSlot] = vaultData;
            }
            else
            {
                trans[invSlot] = null;
                dataTrans[invSlot] = null;
            }

            if (!objects.inventory.Inventory.Execute(trans))
            {
                client.SendPacket(new InvResult { Result = 1 });
                return;
            }
            objects.inventory.Inventory.DatExecute(dataTrans);

            section.Items = vaultItems;
            section.ItemDatas = vaultDatas;
            section.FlushAsync();

            client.SendPacket(new InvResult { Result = 0 });
        }

        private void HandleVaultToVault(Client client, int srcSection, int srcSlot, int expectedSrcType, int destSection, int destSlot)
        {
            if (destSection >= 10 || destSlot < 0 || destSlot >= DbVaultSection.SLOTS_PER_SECTION)
            {
                client.SendPacket(new InvResult { Result = 1 });
                return;
            }

            var srcSectionDb = new DbVaultSection(client.Account, srcSection);
            var srcItems = srcSectionDb.Items;
            var srcDatas = srcSectionDb.ItemDatas;

            if (srcItems[srcSlot] != (ushort)expectedSrcType)
            {
                client.SendPacket(new InvResult { Result = 1 });
                return;
            }

            if (srcSection == destSection)
            {
                // Same section — simple swap
                var tmpType = srcItems[srcSlot];
                var tmpData = srcDatas[srcSlot];
                srcItems[srcSlot] = srcItems[destSlot];
                srcDatas[srcSlot] = srcDatas[destSlot];
                srcItems[destSlot] = tmpType;
                srcDatas[destSlot] = tmpData;

                srcSectionDb.Items = srcItems;
                srcSectionDb.ItemDatas = srcDatas;
                srcSectionDb.FlushAsync();
            }
            else
            {
                // Different sections — load both
                var destSectionDb = new DbVaultSection(client.Account, destSection);
                var destItems = destSectionDb.Items;
                var destDatas = destSectionDb.ItemDatas;

                var tmpType = srcItems[srcSlot];
                var tmpData = srcDatas[srcSlot];
                srcItems[srcSlot] = destItems[destSlot];
                srcDatas[srcSlot] = destDatas[destSlot];
                destItems[destSlot] = tmpType;
                destDatas[destSlot] = tmpData;

                srcSectionDb.Items = srcItems;
                srcSectionDb.ItemDatas = srcDatas;
                srcSectionDb.FlushAsync();

                destSectionDb.Items = destItems;
                destSectionDb.ItemDatas = destDatas;
                destSectionDb.FlushAsync();
            }

            client.SendPacket(new InvResult { Result = 0 });
        }

    }
}
