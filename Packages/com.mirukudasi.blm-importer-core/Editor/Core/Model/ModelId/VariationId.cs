namespace BLMImporter.Editor.Core
{
    /// <summary>アイテムバリエーションのID（booth_item_variations.id）。</summary>
    public sealed class VariationId : ModelIdBase<long>
    {
        public VariationId(long value) : base(value)
        {
        }

        public override bool IsValid => r_Value > 0;
    }
}
