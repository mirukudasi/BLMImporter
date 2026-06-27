using System;
using System.Collections.Generic;

namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// ユーザー作成のリスト
    /// </summary>
    public sealed class ItemListMaster
    {
        public readonly ItemListId r_Id;
        public readonly string r_Title;
        public readonly string r_Description;
        public readonly DateTimeOffset? r_CreatedAt;
        public readonly DateTimeOffset? r_UpdatedAt;
        public readonly HashSet<ItemId> r_ItemIds;

        public ItemListMaster(ItemListId id, string title, string description, string createdAt, string updatedAt, IEnumerable<ItemId> itemIds)
        {
            r_Id = id;
            r_Title = title ?? "";
            r_Description = description ?? "";
            r_CreatedAt = ModelDate.Parse(createdAt);
            r_UpdatedAt = ModelDate.Parse(updatedAt);
            r_ItemIds = itemIds == null ? new HashSet<ItemId>() : new HashSet<ItemId>(itemIds);
        }
    }
}
