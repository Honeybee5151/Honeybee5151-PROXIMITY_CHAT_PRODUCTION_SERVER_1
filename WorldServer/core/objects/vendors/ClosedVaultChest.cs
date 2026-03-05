using Shared.resources;

//editor8182381 — CHANGED: ClosedVaultChest no longer purchasable (vault is now UI-based with unlimited sections)
namespace WorldServer.core.objects.vendors
{
    internal class ClosedVaultChest : SellableObject
    {
        public ClosedVaultChest(GameServer manager, ushort objType) : base(manager, objType)
        {
            Price = 0; //editor8182381 — CHANGED: no longer purchasable
            Currency = CurrencyType.Fame;
        }

        public override void Buy(Player player)
        {
            //editor8182381 — CHANGED: vault is now opened via VaultPanel UI, not purchased
            SendFailed(player, BuyResult.TransactionFailed);
        }
    }
}
