namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// アイテムのバリエーション
    /// </summary>
    public sealed class ItemVariationMaster
    {
        public readonly VariationId r_Id;
        public readonly OrderId r_OrderId;
        public readonly string r_VariationName;

        public ItemVariationMaster(VariationId id, OrderId orderId, string variationName)
        {
            r_Id = id;
            r_OrderId = orderId;
            r_VariationName = variationName ?? "";
        }
    }
}
