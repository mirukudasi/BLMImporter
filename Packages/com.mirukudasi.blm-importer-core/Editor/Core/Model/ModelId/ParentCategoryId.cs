namespace BLMImporter.Editor.Core
{
    /// <summary>親カテゴリのID（parent_categories.id）。</summary>
    public sealed class ParentCategoryId : ModelIdBase<long>
    {
        public ParentCategoryId(long value) : base(value)
        {
        }

        public override bool IsValid => r_Value > 0;
    }
}
