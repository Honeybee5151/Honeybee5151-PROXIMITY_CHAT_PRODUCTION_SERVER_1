using System.Linq;
using Shared.database.account;
using Shared.database.character.inventory;

namespace Shared.database.vault
{
    public class DbVaultSection : RedisObject
    {
        public const int SLOTS_PER_SECTION = 400; // 50 rows × 8 columns

        public string Field { get; private set; }
        public string DataField { get; private set; }

        public ushort[] Items
        {
            get => GetValue<ushort[]>(Field) ?? Enumerable.Repeat((ushort)0xffff, SLOTS_PER_SECTION).ToArray();
            set => SetValue(Field, value);
        }

        public ItemData[] ItemDatas
        {
            get => GetValue<ItemData[]>(DataField) ?? new ItemData[SLOTS_PER_SECTION];
            set => SetValue(DataField, value);
        }

        public DbVaultSection(DbAccount acc, int sectionIndex, bool isAsync = false)
        {
            Field = "section." + sectionIndex;
            DataField = "sectionData." + sectionIndex;

            Init(acc.Database, "vault." + acc.AccountId, null, isAsync);
        }
    }
}
