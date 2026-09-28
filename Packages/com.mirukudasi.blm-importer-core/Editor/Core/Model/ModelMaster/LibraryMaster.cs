using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// BOOTH Library Manager の data.db の全テーブルを Master に変換して持つ。
    /// 各 Master は自分の行から組み立て、他テーブルの値はこの入れ物から引いて生成時に解決する
    /// </summary>
    internal sealed class LibraryMaster
    {
        internal readonly PreferenceMaster r_Preference;
        internal readonly IReadOnlyDictionary<ShopId, ShopMaster> r_Shops;
        internal readonly IReadOnlyDictionary<ParentCategoryId, ParentCategoryMaster> r_ParentCategories;
        internal readonly IReadOnlyDictionary<SubCategoryId, SubCategoryMaster> r_SubCategories;
        internal readonly ILookup<ItemId, ItemTagMaster> r_TagsByItem;
        internal readonly ILookup<ItemId, ItemVariationMaster> r_VariationsByItem;
        internal readonly IReadOnlyDictionary<ItemId, RegisteredItemMaster> r_RegisteredItems;
        internal readonly IReadOnlyDictionary<ItemId, ItemUpdateHistoryMaster> r_ItemUpdateHistories;
        internal readonly ILookup<ItemListId, ListItemMaster> r_ListItemsByList;
        internal readonly ILookup<SmartListId, SmartListCriteriaMaster> r_CriteriaBySmartList;
        internal readonly ILookup<SmartListId, SmartListTagMaster> r_TagsBySmartList;
        internal readonly IReadOnlyList<TagMaster> r_Tags;
        internal readonly IReadOnlyDictionary<ItemId, ItemMaster> r_Items;
        internal readonly IReadOnlyDictionary<ItemListId, ItemListMaster> r_Lists;
        internal readonly IReadOnlyDictionary<SmartListId, SmartListMaster> r_SmartLists;
        internal readonly IReadOnlyDictionary<NotificationId, NotificationMaster> r_Notifications;
        internal readonly IReadOnlyDictionary<UserItemId, UserItemMaster> r_UserItems;

        // 後の Master が前の Master を参照して値を解決するため、この順序で組み立てる
        internal LibraryMaster(MiniSqlite database)
        {
            r_Preference = ReadRows(database, "preferences", row => new PreferenceMaster(row)).FirstOrDefault() ?? new PreferenceMaster();
            r_Shops = ToFirstWinsDictionary(ReadRows(database, "shops", row => new ShopMaster(row)), shop => shop.r_Id);
            r_ParentCategories = ToFirstWinsDictionary(ReadRows(database, "parent_categories", row => new ParentCategoryMaster(row)), parentCategory => parentCategory.r_Id);
            r_SubCategories = ToFirstWinsDictionary(ReadRows(database, "sub_categories", row => new SubCategoryMaster(row)), subCategory => subCategory.r_Id);
            r_TagsByItem = ReadRows(database, "booth_item_tag_relations", row => new ItemTagMaster(row)).ToLookup(tag => tag.r_ItemId);
            // booth_item_variations をアイテム単位のバリエーション一覧にまとめる
            r_VariationsByItem = ReadRows(database, "booth_item_variations", row => new ItemVariationMaster(row)).ToLookup(variation => variation.r_ItemId);
            // booth_item_id -> 日時列 をアイテム単位にまとめる
            r_RegisteredItems = ToLastWinsDictionary(ReadRows(database, "registered_items", row => new RegisteredItemMaster(row)), registeredItem => registeredItem.r_ItemId);
            r_ItemUpdateHistories = ToLastWinsDictionary(ReadRows(database, "booth_item_update_history", row => new ItemUpdateHistoryMaster(row)), history => history.r_ItemId);
            // 先に list_items から所属アイテムIDを集める
            r_ListItemsByList = ReadRows(database, "list_items", row => new ListItemMaster(row)).ToLookup(listItem => listItem.r_ListId);
            r_CriteriaBySmartList = ReadRows(database, "smart_list_criteria", row => new SmartListCriteriaMaster(row)).ToLookup(criteria => criteria.r_SmartListId);
            r_TagsBySmartList = ReadRows(database, "smart_list_tags", row => new SmartListTagMaster(row)).ToLookup(tag => tag.r_SmartListId);
            r_Tags = ReadRows(database, "booth_tags", row => new TagMaster(row));
            r_Items = ToFirstWinsDictionary(ReadRows(database, "booth_items", row => new ItemMaster(row, this)), item => item.r_Id);
            r_Lists = ToFirstWinsDictionary(ReadRows(database, "lists", row => new ItemListMaster(row, this)), list => list.r_Id);
            r_SmartLists = ToFirstWinsDictionary(ReadRows(database, "smart_lists", row => new SmartListMaster(row, this)), smartList => smartList.r_Id);
            r_Notifications = ToFirstWinsDictionary(ReadRows(database, "notifications", row => new NotificationMaster(row)), notification => notification.r_Id);
            r_UserItems = ToFirstWinsDictionary(ReadRows(database, "user_item_info", row => new UserItemMaster(row, this)), userItem => userItem.r_Id);
        }

        internal ShopMaster FindShop(ShopId id)
        {
            r_Shops.TryGetValue(id, out var shop);
            return shop;
        }

        internal SubCategoryMaster FindSubCategory(SubCategoryId id)
        {
            r_SubCategories.TryGetValue(id, out var subCategory);
            return subCategory;
        }

        internal ParentCategoryMaster FindParentCategory(ParentCategoryId id)
        {
            r_ParentCategories.TryGetValue(id, out var parentCategory);
            return parentCategory;
        }

        internal RegisteredItemMaster FindRegisteredItem(ItemId id)
        {
            r_RegisteredItems.TryGetValue(id, out var registeredItem);
            return registeredItem;
        }

        internal ItemUpdateHistoryMaster FindItemUpdateHistory(ItemId id)
        {
            r_ItemUpdateHistories.TryGetValue(id, out var history);
            return history;
        }

        // 1行の変換に失敗しても読み込み全体は止めず、その行だけ飛ばしてログを残す。読み込む条件を満たさない行も飛ばす
        private static List<T> ReadRows<T>(MiniSqlite database, string table, Func<MiniSqlite.Row, T> build) where T : class, ILibraryMasterRow
        {
            var output = new List<T>();
            foreach (var row in database.SelectAll(table)) {
                try {
                    var master = build(row);
                    if (master.IsValid) {
                        output.Add(master);
                    }
                }
                catch (Exception exception) {
                    Debug.LogError($"{table} の行の読み込みに失敗したためスキップします (rowid={row.m_RowId})\n{exception}");
                }
            }
            return output;
        }

        // 挿入順を保ったまま辞書化する。同一IDが複数あれば最初の1件を採用する
        private static Dictionary<TKey, TValue> ToFirstWinsDictionary<TKey, TValue>(IEnumerable<TValue> source, Func<TValue, TKey> keySelector)
        {
            var dictionary = new Dictionary<TKey, TValue>();
            foreach (var value in source) {
                var key = keySelector(value);
                if (!dictionary.ContainsKey(key)) {
                    dictionary[key] = value;
                }
            }
            return dictionary;
        }

        // 同一IDが複数あれば最後の1件で上書きする
        private static Dictionary<TKey, TValue> ToLastWinsDictionary<TKey, TValue>(IEnumerable<TValue> source, Func<TValue, TKey> keySelector)
        {
            var dictionary = new Dictionary<TKey, TValue>();
            foreach (var value in source) {
                dictionary[keySelector(value)] = value;
            }
            return dictionary;
        }
    }
}
