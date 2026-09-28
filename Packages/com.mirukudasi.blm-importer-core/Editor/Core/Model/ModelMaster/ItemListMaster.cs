using System;
using System.Collections.Generic;
using System.Linq;

namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// ユーザー作成のリスト
    /// </summary>
    public sealed class ItemListMaster : ILibraryMasterRow
    {
        public readonly ItemListId r_Id;
        public readonly string r_Title;
        public readonly string r_Description;
        public readonly DateTimeOffset? r_CreatedAt;
        public readonly DateTimeOffset? r_UpdatedAt;
        public readonly HashSet<ItemId> r_ItemIds;

        internal ItemListMaster(MiniSqlite.Row row, LibraryMaster library)
        {
            r_Id = new ItemListId(row.m_RowId);
            r_Title = row.GetString("title") ?? "";
            r_Description = row.GetString("description") ?? "";
            r_CreatedAt = ModelDate.Parse(row.GetString("created_at") ?? "");
            r_UpdatedAt = ModelDate.Parse(row.GetString("updated_at") ?? "");
            r_ItemIds = new HashSet<ItemId>(library.r_ListItemsByList[r_Id].Select(listItem => listItem.r_ItemId));
        }

        bool ILibraryMasterRow.IsValid => true;
    }
}
