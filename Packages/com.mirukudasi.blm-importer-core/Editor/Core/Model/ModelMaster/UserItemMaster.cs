using System;

namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// ユーザーが手動で追加したカスタムアイテム
    /// </summary>
    public sealed class UserItemMaster : ILibraryMasterRow
    {
        public readonly UserItemId r_Id;
        public readonly string r_Name;
        public readonly string r_ShopName;
        public readonly string r_ThumbnailFilename;
        public readonly SubCategoryId r_SubCategoryId;
        public readonly string r_SubCategoryName;
        public readonly string r_Description;
        public readonly bool r_Adult;
        public readonly DateTimeOffset? r_CreatedAt;
        public readonly DateTimeOffset? r_UpdatedAt;

        internal UserItemMaster(MiniSqlite.Row row, LibraryMaster library)
        {
            r_Id = new UserItemId(row.m_RowId);
            r_Name = row.GetString("name") ?? "";
            r_ShopName = row.GetString("shop_name") ?? "";
            r_ThumbnailFilename = row.GetString("thumbnail_filename") ?? "";
            r_SubCategoryId = new SubCategoryId(row.GetLong("sub_category", 0));
            r_SubCategoryName = library.FindSubCategory(r_SubCategoryId)?.r_Name ?? "";
            r_Description = row.GetString("description") ?? "";
            r_Adult = row.GetLong("adult", 0) != 0;
            r_CreatedAt = ModelDate.Parse(row.GetString("created_at") ?? "");
            r_UpdatedAt = ModelDate.Parse(row.GetString("updated_at") ?? "");
        }

        bool ILibraryMasterRow.IsValid => true;
    }
}
