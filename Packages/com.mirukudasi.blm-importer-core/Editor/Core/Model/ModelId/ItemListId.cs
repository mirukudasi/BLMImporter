namespace BLMImporter.Editor.Core
{
    /// <summary>ユーザー作成リストのID（lists.id）。</summary>
    public sealed class ItemListId : ModelIdBase<long>
    {
        internal ItemListId(long value) : base(value)
        {
        }

        public override bool IsValid => r_Value > 0;
    }
}
