using System;
using System.Collections.Generic;
using System.Linq;

namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// アイテムの不変マスタデータ
    /// ローカルファイル状態や派生値は持たない（それらは <see cref="ItemRuntime"/> 側）。
    /// </summary>
    public sealed class ItemMaster
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

        public ItemMaster(
            ItemId id, string name, ShopId shopId, string shopName, string shopThumbnailUrl,
            SubCategoryId subCategoryId, string subCategoryName, ParentCategoryId parentCategoryId, string parentCategoryName,
            bool adult, string description, string thumbnailUrl,
            string publishedAt, string updatedAt, string libraryUpdatedAt, string registeredAt,
            IEnumerable<string> tags, IEnumerable<OrderId> orderIds, IEnumerable<ItemVariationMaster> variations)
        {
            r_Id = id;
            r_Name = name ?? "";
            r_ShopId = shopId;
            r_ShopName = shopName ?? "";
            r_ShopThumbnailUrl = new Url(shopThumbnailUrl);
            r_SubCategoryId = subCategoryId;
            r_SubCategoryName = subCategoryName ?? "";
            r_ParentCategoryId = parentCategoryId;
            r_ParentCategoryName = parentCategoryName ?? "";
            r_Adult = adult;
            r_Description = description ?? "";
            r_ThumbnailUrl = new Url(thumbnailUrl);
            r_PublishedAt = ModelDate.Parse(publishedAt);
            r_UpdatedAt = ModelDate.Parse(updatedAt);
            r_LibraryUpdatedAt = ModelDate.Parse(libraryUpdatedAt);
            r_RegisteredAt = ModelDate.Parse(registeredAt);
            // 防御的コピーで生成後の変更を防ぐ
            r_Tags = (tags ?? Enumerable.Empty<string>()).ToArray();
            r_OrderIds = (orderIds ?? Enumerable.Empty<OrderId>()).ToArray();
            r_Variations = (variations ?? Enumerable.Empty<ItemVariationMaster>()).ToArray();
        }
    }
}
