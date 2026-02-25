using System;
using System.Linq;
using System.Net.Http;
using Shared;
using Shared.database.character.inventory;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.core.worlds.impl;
using WorldServer.networking;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.net.handlers
{
    public class LoadHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.LOAD;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime tickTime)
        {
            var charId = rdr.ReadInt32();

            if (client.State != ProtocolState.Handshaked)
                return;

            var target = client.GameServer.WorldManager.GetWorld(client.TargetWorld);

            if (target == null)
            {
                client.SendFailure($"Unable to find world: {client.TargetWorld}", FailureMessage.MessageWithDisconnect);
                return;
            }

            client.Character = client.GameServer.Database.LoadCharacter(client.Account, charId);

            if (client.Character == null)
                client.SendFailure("Failed to load character", FailureMessage.MessageWithDisconnect);
            else if (client.Character.Dead)
                client.SendFailure("Character is dead", FailureMessage.MessageWithDisconnect);
            else
            {
                var x = 0;
                var y = 0;

                var spawnRegions = target.GetSpawnPoints();
                if (spawnRegions.Length > 0)
                {
                    var sRegion = Random.Shared.NextLength(spawnRegions);
                    x = sRegion.Key.X;
                    y = sRegion.Key.Y;
                }

                // Community dungeon: backup real character and override with temp classless character
                if (target.IsCommunityDungeon)
                    SetupCommunityDungeonCharacter(client, target);

                var player = client.Player = target.CreateNewPlayer(client, client.Character.ObjectType, x, y);

                // Community dungeon: override SlotTypes to all zeros (classless — any item fits any slot)
                if (target.IsCommunityDungeon)
                {
                    for (int i = 0; i < player.SlotTypes.Length; i++)
                        player.SlotTypes[i] = 0;
                }

                client.SendPacket(new CreateSuccessMessage(player.Id, client.Character.CharId));

                if(target is RealmWorld realm)
                    realm.RealmManager.OnPlayerEntered(player);

                client.State = ProtocolState.Ready;
                client.GameServer.ConnectionManager.ClientConnected(client);

                // Fire-and-forget: check for pending donation purchases
                _ = CheckPendingPurchases(client.Account.Name);
            }
        }

        private static void SetupCommunityDungeonCharacter(Client client, World target)
        {
            var chr = client.Character;
            var gameData = client.GameServer.Resources.GameData;

            // Backup the real character state
            client.DungeonBackup = new CharacterBackup
            {
                ObjectType = chr.ObjectType,
                Items = (ushort[])chr.Items.Clone(),
                Datas = chr.Datas != null ? (ItemData[])chr.Datas.Clone() : null,
                Stats = (int[])chr.Stats.Clone(),
                Level = chr.Level,
                Experience = chr.Experience,
                Health = chr.Health,
                MP = chr.MP,
                Fame = chr.Fame,
                Skin = chr.Skin,
                Tex1 = chr.Tex1,
                Tex2 = chr.Tex2,
                HasBackpack = chr.HasBackpack,
                HealthStackCount = chr.HealthStackCount,
                MagicStackCount = chr.MagicStackCount
            };

            // Clear inventory (0xffff = empty slot)
            for (int i = 0; i < chr.Items.Length; i++)
                chr.Items[i] = 0xffff;
            if (chr.Datas != null)
                for (int i = 0; i < chr.Datas.Length; i++)
                    chr.Datas[i] = null;

            // Give starting equipment from dungeon config
            if (target.StartingEquipment != null)
            {
                for (int i = 0; i < target.StartingEquipment.Length && i < chr.Items.Length; i++)
                {
                    var itemName = target.StartingEquipment[i].Trim();
                    if (!string.IsNullOrEmpty(itemName) && gameData.IdToObjectType.TryGetValue(itemName, out var itemType))
                        chr.Items[i] = itemType;
                }
            }

            // Set level 20, max stats for a warrior-like baseline
            chr.Level = 20;
            chr.Experience = 0;
            // Stats: [HP, MP, Att, Def, Spd, Dex, Vit, Wis]
            chr.Stats = new int[] { 770, 252, 75, 25, 50, 50, 75, 50 };
            chr.Health = 770;
            chr.MP = 252;

            // Override class to Warrior (0x031d) so Skeleton Warrior skin (0x745E) works for all classes
            chr.ObjectType = 0x031d;
            chr.Skin = 0x745E;
            chr.Tex1 = 0;
            chr.Tex2 = 0;

            // Clear potion stacks and backpack
            chr.HasBackpack = false;
            chr.HealthStackCount = 0;
            chr.MagicStackCount = 0;
        }

        private static readonly HttpClient _httpClient = new HttpClient();

        private static async System.Threading.Tasks.Task CheckPendingPurchases(string gameName)
        {
            try
            {
                var webhookSecret = Environment.GetEnvironmentVariable("WEBHOOK_SECRET") ?? "";
                if (string.IsNullOrEmpty(webhookSecret)) return;

                var url = $"http://admindashboard:8080/api/donations/check-pending?gameName={Uri.EscapeDataString(gameName)}";
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("X-Webhook-Secret", webhookSecret);

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                    Console.WriteLine($"[Donations] Checked pending purchases for '{gameName}'");
                else
                    Console.WriteLine($"[Donations] Check-pending failed for '{gameName}': {response.StatusCode}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Donations] Check-pending error for '{gameName}': {ex.Message}");
            }
        }
    }
}
