namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// スマートリストの絞り込みに使うタグ1件
    /// </summary>
    internal sealed class SmartListTagMaster : ILibraryMasterRow
    {
        public readonly SmartListId r_SmartListId;
        public readonly string r_Tag;

        internal SmartListTagMaster(MiniSqlite.Row row)
        {
            r_SmartListId = new SmartListId(row.GetLong("smart_list_id", 0));
            r_Tag = row.GetString("tag");
        }

        bool ILibraryMasterRow.IsValid => !string.IsNullOrEmpty(r_Tag);
    }
}
