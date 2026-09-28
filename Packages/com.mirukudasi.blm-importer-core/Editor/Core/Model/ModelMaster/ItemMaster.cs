using System;
using System.Collections.Generic;
using System.Linq;

namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// アイテムの不変マスタデータ
    /// ローカルファイル状態や派生値は持たない（それらは <see cref="ItemRuntime"/> 側）。
    /// </summary>
    public sealed class ItemMaster : ILibraryMasterRow
    {
        public readonly ItemId r_Id;
        public readonly string r_Name;
        public readonly ShopId r_ShopId;
        public readonly string r_ShopName;
        public readonly Url r_ShopThumbnailUrl;
        public readonly SubCategoryId r_SubCategoryId;
        public readonly string r_SubCategoryName;
        public readonly ParentCategoryId r_ParentCategoryId;
        public readonly string r_ParentCategoryName;
        public readonly bool r_Adult;
        public readonly string r_Description;
        public readonly Url r_ThumbnailUrl;
        // BOOTH公開日時 / BOOTH側メタ更新日時 / ライブラリ側更新日時 / ライブラリ登録日時
        public readonly DateTimeOffset? r_PublishedAt;
        public readonly DateTimeOffset? r_UpdatedAt;
        public readonly DateTimeOffset? r_LibraryUpdatedAt;
        public readonly DateTimeOffset? r_RegisteredAt;
        public readonly IReadOnlyList<string> r_Tags;
        public readonly IReadOnlyList<OrderId> r_OrderIds;
        public readonly IReadOnlyList<ItemVariationMaster> r_Variations;

        internal ItemMaster(MiniSqlite.Row row, LibraryMaster library)
        {
            r_Id = new ItemId(row.m_RowId);
            r_Name = row.GetString("name") ?? "";
            r_ShopId = new ShopId(row.GetString("shop_subdomain") ?? "");

            r_ShopName = r_ShopId.r_Value;
            r_ShopThumbnailUrl = new Url("");
            var shop = library.FindShop(r_ShopId);
            if (shop != null) {
                r_ShopName = shop.r_Name;
                r_ShopThumbnailUrl = shop.r_ThumbnailUrl;
            }

            r_SubCategoryId = new SubCategoryId(row.GetLong("sub_category", 0));
            r_SubCategoryName = "";
            r_ParentCategoryId = new ParentCategoryId(0);
            r_ParentCategoryName = "";
            var subCategory = library.FindSubCategory(r_SubCategoryId);
            if (subCategory != null) {
                r_SubCategoryName = subCategory.r_Name;
                r_ParentCategoryId = subCategory.r_ParentCategoryId;
                r_ParentCategoryName = library.FindParentCategory(r_ParentCategoryId)?.r_Name ?? "";
            }

            r_Adult = row.GetLong("adult", 0) != 0;
            r_Description = row.GetString("description") ?? "";
            r_ThumbnailUrl = new Url(row.GetString("thumbnail_url") ?? "");
            r_PublishedAt = ModelDate.Parse(row.GetString("published_at"));
            r_UpdatedAt = ModelDate.Parse(row.GetString("updated_at"));
            r_LibraryUpdatedAt = ModelDate.Parse(library.FindItemUpdateHistory(r_Id)?.r_LastUpdatedAt);
            r_RegisteredAt = ModelDate.Parse(library.FindRegisteredItem(r_Id)?.r_CreatedAt);

            // 防御的コピーで生成後の変更を防ぐ
            r_Tags = library.r_TagsByItem[r_Id].Select(tag => tag.r_Tag).ToArray();
            r_Variations = library.r_VariationsByItem[r_Id].ToArray();
            r_OrderIds = r_Variations.Select(variation => variation.r_OrderId).Where(orderId => orderId.IsValid).Distinct().ToArray();
        }

        bool ILibraryMasterRow.IsValid => true;
    }
}
