namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// ライブラリ側でアイテムを最後に更新した日時
    /// 日時は文字列のまま持ち、アイテム側で解釈する
    /// </summary>
    internal sealed class ItemUpdateHistoryMaster : ILibraryMasterRow
    {
        public readonly ItemId r_ItemId;
        public readonly string r_LastUpdatedAt;

        internal ItemUpdateHistoryMaster(MiniSqlite.Row row)
        {
            r_ItemId = new ItemId(row.GetLong("booth_item_id", 0));
            r_LastUpdatedAt = row.GetString("last_updated_at") ?? "";
        }

        bool ILibraryMasterRow.IsValid => r_ItemId.r_Value != 0;
    }
}
