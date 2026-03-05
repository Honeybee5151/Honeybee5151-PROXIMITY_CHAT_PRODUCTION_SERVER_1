using System;
using System.Collections.Generic;
using System.Linq;
using Shared.database.vault;
using Shared.resources;
using WorldServer.core.miscfile;
using WorldServer.core.objects;
using WorldServer.core.objects.containers;
using WorldServer.core.objects.inventory;
using WorldServer.core.objects.vendors;
using WorldServer.core.structures;
using WorldServer.networking;

namespace WorldServer.core.worlds.impl
{
    public sealed class VaultWorld : World
    {
        public int AccountId { get; private set; }
        public Client Client { get; private set; }

        public VaultWorld(GameServer gameServer, int id, WorldResource resource, World parent) : base(gameServer, id, resource, parent)
        {
        }

        //editor8182381 — DELETED: AddChest() method (vault containers replaced by VaultScreen UI)

        public override bool AllowedAccess(Client client) => (AccountId == client.Account.AccountId) || client.Account.Admin;

        public override void LeaveWorld(Entity entity)
        {
            base.LeaveWorld(entity);

            if (entity is Player)
                FlagForClose();

            if (entity.ObjectType != 0x0744 && entity.ObjectType != 0xa011)
                return;

            var objType = entity.ObjectType == 0x0744 ? 0x0743 : 0xa012;
            var x = new StaticObject(Client.GameServer, (ushort)objType, null, true, false, false) { Size = 65 };
            x.Move(entity.X, entity.Y);

            EnterWorld(x);
        }


        public void SetOwner(int accountId) => AccountId = accountId;

        public void SetClient(Client client)
        {
            Client = client;
            AccountId = Client.Account.AccountId;

            var vaultChestPosition = new List<IntPoint>();
            var giftChestPosition = new List<IntPoint>();
            var specialChestPosition = new List<IntPoint>();

            var spawn = new IntPoint(0, 0);
            var w = Map.Width;
            var h = Map.Height;

            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var tile = Map[x, y];

                    var pos = new IntPoint(x, y);
                    switch (tile.Region)
                    {
                        case TileRegion.Spawn:
                            spawn = pos;
                            break;

                        case TileRegion.Vault:
                            vaultChestPosition.Add(pos);
                            break;

                        case TileRegion.Gifting_Chest:
                            giftChestPosition.Add(pos);
                            break;

                        case TileRegion.Special_Chest:
                            specialChestPosition.Add(pos);
                            break;
                    }
                }

            vaultChestPosition.Sort((x, y) => Comparer<int>.Default.Compare((x.X - spawn.X) * (x.X - spawn.X) + (x.Y - spawn.Y) * (x.Y - spawn.Y), (y.X - spawn.X) * (y.X - spawn.X) + (y.Y - spawn.Y) * (y.Y - spawn.Y)));
            giftChestPosition.Sort((x, y) => Comparer<int>.Default.Compare((x.X - spawn.X) * (x.X - spawn.X) + (x.Y - spawn.Y) * (x.Y - spawn.Y), (y.X - spawn.X) * (y.X - spawn.X) + (y.Y - spawn.Y) * (y.Y - spawn.Y)));
            specialChestPosition.Sort((x, y) => Comparer<int>.Default.Compare((x.X - spawn.X) * (x.X - spawn.X) + (x.Y - spawn.Y) * (x.Y - spawn.Y), (y.X - spawn.X) * (y.X - spawn.X) + (y.Y - spawn.Y) * (y.Y - spawn.Y)));

            //editor8182381 — CHANGED: place single vault chest object (UI-based vault replaces per-chest containers)
            if (vaultChestPosition.Count > 0)
            {
                CreateNewEntity(0x0505, vaultChestPosition[0].X + 0.5f, vaultChestPosition[0].Y + 0.5f);
            }

            var gifts = Client.Account.Gifts.ToList();
            while (gifts.Count > 0 && giftChestPosition.Count > 0)
            {
                var c = Math.Min(8, gifts.Count);
                var items = gifts.GetRange(0, c);

                gifts.RemoveRange(0, c);

                if (c < 8)
                    items.AddRange(Enumerable.Repeat(ushort.MaxValue, 8 - c));

                var con = new GiftChest(Client.GameServer, 0x0744)
                {
                    BagOwners = new int[] { Client.Account.AccountId },
                    Size = 100
                };
                con.Inventory.SetItems(con.Inventory.ConvertTypeToItemArray(items));
                con.Move(giftChestPosition[0].X + 0.5f, giftChestPosition[0].Y + 0.5f);

                EnterWorld(con);

                giftChestPosition.RemoveAt(0);
            }

            foreach (var i in giftChestPosition)
            {
                var x = new StaticObject(Client.GameServer, 0x0743, null, true, false, false) { Size = 100 };
                x.Move(i.X + 0.5f, i.Y + 0.5f);

                EnterWorld(x);
            }

            GenerateSpecialChests(specialChestPosition);
        }

        private void GenerateSpecialChests(List<IntPoint> specialChestPosition)
        {
            for (var i = 0; i < specialChestPosition.Count; i++)
            {
                var specialVault = new DbSpecialVault(Client.Account, i);
                if (!specialVault.GetItems())
                    continue;

                var con = new SpecialChest(Client.GameServer, 0xa011, null, false, specialVault)
                {
                    BagOwners = new int[] { AccountId },
                    Size = 65
                };
                con.Inventory.SetItems(con.Inventory.ConvertTypeToItemArray(specialVault.Items));
                con.Inventory.SetDataItems(specialVault.ItemDatas);
                con.Inventory.InventoryChanged += (sender, e) => SaveChest(((Inventory)sender).Parent);
                con.Move(specialChestPosition[0].X + 0.5f, specialChestPosition[0].Y + 0.5f);

                EnterWorld(con);
                //Console.WriteLine($"Chest With Items, Pos: X: {specialChestPosition[0].X}, Y: {specialChestPosition[0].Y}");
                specialChestPosition.RemoveAt(0);
            }

            foreach (var i in specialChestPosition)
            {
                var x = new StaticObject(Client.GameServer, 0xa012, null, true, false, false) { Size = 65 };
                x.Move(i.X + 0.5f, i.Y + 0.5f);

                EnterWorld(x);
                //Console.WriteLine($"Empty Chest, Pos: X: {i.X}, Y: {i.Y}");
            }
        }

        private void SaveChest(IContainer chest)
        {
            var dbLink = chest?.DbLink;

            if (dbLink == null)
                return;

            dbLink.Items = chest.Inventory.GetItemTypes();
            dbLink.ItemDatas = chest.Inventory.Data.GetDatas();
            dbLink.FlushAsync();
        }
    }
}
