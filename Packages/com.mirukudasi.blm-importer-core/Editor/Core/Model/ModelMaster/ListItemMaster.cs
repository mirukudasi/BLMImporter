namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// ユーザー作成のリストに所属するアイテム1件
    /// </summary>
    internal sealed class ListItemMaster : ILibraryMasterRow
    {
        public readonly ItemListId r_ListId;
        // アイテム ID として解釈できない行は null
        public readonly ItemId r_ItemId;

        internal ListItemMaster(MiniSqlite.Row row)
        {
            r_ListId = new ItemListId(row.GetLong("list_id", 0));
            r_ItemId = ParseRegisteredItemId(row.GetString("item_id") ?? "");
        }

        bool ILibraryMasterRow.IsValid => r_ItemId != null;

        // registered_items.id は "b" + booth_item_id 形式
        private static ItemId ParseRegisteredItemId(string registeredId)
        {
            if (string.IsNullOrEmpty(registeredId) || registeredId[0] != 'b') {
                return null;
            }
            var parsed = long.TryParse(registeredId.Substring(1), out var value);
            if (parsed) {
                return new ItemId(value);
            }
            return null;
        }
    }
}
