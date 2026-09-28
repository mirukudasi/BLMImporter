namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// BOOTH のサブカテゴリ
    /// 所属する親カテゴリの ID を持つ
    /// </summary>
    internal sealed class SubCategoryMaster : ILibraryMasterRow
    {
        public readonly SubCategoryId r_Id;
        public readonly string r_Name;
        public readonly ParentCategoryId r_ParentCategoryId;

        internal SubCategoryMaster(MiniSqlite.Row row)
        {
            r_Id = new SubCategoryId(row.m_RowId);
            r_Name = row.GetString("name") ?? "";
            r_ParentCategoryId = new ParentCategoryId(row.GetLong("parent_category_id", 0));
        }

        bool ILibraryMasterRow.IsValid => true;
    }
}
