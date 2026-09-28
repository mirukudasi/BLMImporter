namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// BOOTH の親カテゴリ
    /// </summary>
    internal sealed class ParentCategoryMaster : ILibraryMasterRow
    {
        public readonly ParentCategoryId r_Id;
        public readonly string r_Name;

        internal ParentCategoryMaster(MiniSqlite.Row row)
        {
            r_Id = new ParentCategoryId(row.m_RowId);
            r_Name = row.GetString("name") ?? "";
        }

        bool ILibraryMasterRow.IsValid => true;
    }
}
