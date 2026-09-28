namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// booth_tags（マスタのタグ一覧）の1件
    /// </summary>
    internal sealed class TagMaster : ILibraryMasterRow
    {
        public readonly string r_Name;

        internal TagMaster(MiniSqlite.Row row)
        {
            r_Name = row.GetString("name");
        }

        bool ILibraryMasterRow.IsValid => !string.IsNullOrEmpty(r_Name);
    }
}
