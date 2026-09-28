namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// アイテムに付いたタグ1件
    /// </summary>
    internal sealed class ItemTagMaster : ILibraryMasterRow
    {
        public readonly ItemId r_ItemId;
        public readonly string r_Tag;

        internal ItemTagMaster(MiniSqlite.Row row)
        {
            r_ItemId = new ItemId(row.GetLong("booth_item_id", 0));
            r_Tag = row.GetString("tag");
        }

        bool ILibraryMasterRow.IsValid => r_ItemId.r_Value != 0 && !string.IsNullOrEmpty(r_Tag);
    }
}
