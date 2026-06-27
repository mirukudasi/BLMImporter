namespace BLMImporter.Editor.Core
{
    /// <summary>サブカテゴリのID（sub_categories.id）。</summary>
    public sealed class SubCategoryId : ModelIdBase<long>
    {
        public SubCategoryId(long value) : base(value)
        {
        }

        public override bool IsValid => r_Value > 0;
    }
}
