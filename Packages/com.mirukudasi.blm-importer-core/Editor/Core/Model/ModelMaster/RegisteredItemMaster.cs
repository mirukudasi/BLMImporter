namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// アイテムをライブラリへ登録した日時
    /// 日時は文字列のまま持ち、アイテム側で解釈する
    /// </summary>
    internal sealed class RegisteredItemMaster : ILibraryMasterRow
    {
        public readonly ItemId r_ItemId;
        public readonly string r_CreatedAt;

        internal RegisteredItemMaster(MiniSqlite.Row row)
        {
            r_ItemId = new ItemId(row.GetLong("booth_item_id", 0));
            r_CreatedAt = row.GetString("created_at") ?? "";
        }

        bool ILibraryMasterRow.IsValid => r_ItemId.r_Value != 0;
    }
}
