namespace BLMImporter.Editor.Core
{
    /// <summary>BOOTH注文のID（booth_item_variations.order_id）。</summary>
    public sealed class OrderId : ModelIdBase<long>
    {
        internal OrderId(long value) : base(value)
        {
        }

        public override bool IsValid => r_Value > 0;
    }
}
