using System;

namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// ユーザーが手動で追加したカスタムアイテム
    /// </summary>
    public sealed class UserItemMaster
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

        public UserItemMaster(UserItemId id, string name, string shopName, string thumbnailFilename, SubCategoryId subCategoryId, string subCategoryName, string description, bool adult, string createdAt, string updatedAt)
        {
            r_Id = id;
            r_Name = name ?? "";
            r_ShopName = shopName ?? "";
            r_ThumbnailFilename = thumbnailFilename ?? "";
            r_SubCategoryId = subCategoryId;
            r_SubCategoryName = subCategoryName ?? "";
            r_Description = description ?? "";
            r_Adult = adult;
            r_CreatedAt = ModelDate.Parse(createdAt);
            r_UpdatedAt = ModelDate.Parse(updatedAt);
        }
    }
}
