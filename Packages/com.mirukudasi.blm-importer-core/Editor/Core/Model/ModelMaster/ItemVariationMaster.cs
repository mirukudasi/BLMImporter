namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// アイテムのバリエーション
    /// </summary>
    public sealed class ItemVariationMaster : ILibraryMasterRow
    {
        public readonly VariationId r_Id;
        public readonly OrderId r_OrderId;
        public readonly string r_VariationName;
        public readonly ItemId r_ItemId;

        internal ItemVariationMaster(MiniSqlite.Row row)
        {
            r_Id = new VariationId(row.m_RowId);
            r_OrderId = new OrderId(row.GetLong("order_id", 0));
            r_VariationName = row.GetString("variation_name") ?? "";
            r_ItemId = new ItemId(row.GetLong("booth_item_id", 0));
        }

        bool ILibraryMasterRow.IsValid => r_ItemId.r_Value != 0;
    }
}
