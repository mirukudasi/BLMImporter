namespace BLMImporter.Editor.Core
{
    /// <summary>BOOTHアイテムのID（booth_items.id）。</summary>
    public sealed class ItemId : ModelIdBase<long>
    {
        public static readonly ItemId Undefined = new ItemId(0);

        public ItemId(long value) : base(value)
        {
        }

        public override bool IsValid => r_Value > 0;
    }
}
