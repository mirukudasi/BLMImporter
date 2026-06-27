using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace BLMImporter.Editor.Core
{
    /// <summary>BOOTH Library Manager の data.db とライブラリフォルダを読み込み、Master/Runtime 構造で返す。</summary>
    public static class LibraryData
    {
        /// <summary>
        /// DBから引いたID・名前の対応表をまとめて持ち回る
        /// </summary>
        private sealed class LookupTables
        {
            public readonly Dictionary<ShopId, ShopMaster> r_ShopsById = new Dictionary<ShopId, ShopMaster>();
            public readonly Dictionary<SubCategoryId, string> r_SubCategoryNames = new Dictionary<SubCategoryId, string>();
            public readonly Dictionary<SubCategoryId, ParentCategoryId> r_SubCategoryParents = new Dictionary<SubCategoryId, ParentCategoryId>();
            public readonly Dictionary<ParentCategoryId, string> r_ParentCategoryNames = new Dictionary<ParentCategoryId, string>();
            public Dictionary<ItemId, List<string>> m_TagsByItem = new Dictionary<ItemId, List<string>>();
            public Dictionary<ItemId, List<ItemVariationMaster>> m_VariationsByItem = new Dictionary<ItemId, List<ItemVariationMaster>>();
            public Dictionary<ItemId, string> m_RegisteredAtByItem = new Dictionary<ItemId, string>();
            public Dictionary<ItemId, string> m_LibraryUpdatedAtByItem = new Dictionary<ItemId, string>();
        }

        private sealed class Preferences
        {
            public string r_LibraryPath = "";
            public string r_Theme = "";
            public string r_Language = "";
        }

        /// <summary>
        /// data.db の配置場所
        /// </summary>
        public static string DefaultDatabasePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pm.booth.library-manager", "data.db");

        /// <summary>DBを読み込み、ライブラリフォルダを走査してスナップショットを構築する。</summary>
        public static LibraryRuntimeSnapshot LoadRuntime(string databasePath)
        {
            var database = new MiniSqlite(databasePath);
            var preferences = ReadPreferences(database);
            var shops = BuildShops(database);
            var lookups = BuildLookups(database, shops);

            // 1件でも壊れたアイテムがあっても全体の読み込みが止まらないよう、行ごとに安全に組み立てる
            var items = BuildEachRow(database, "booth_items",
                row => BuildItemRuntime(BuildItemMaster(row, lookups), preferences.r_LibraryPath));

            return new LibraryRuntimeSnapshot(
                preferences.r_LibraryPath, preferences.r_Theme, preferences.r_Language,
                items,
                BuildLists(database).Select(master => new ItemListRuntime(master)),
                shops.Select(master => new ShopRuntime(master)),
                BuildAllTags(database),
                BuildNotifications(database).Select(master => new NotificationRuntime(master)),
                BuildSmartLists(database).Select(master => new SmartListRuntime(master)),
                BuildUserItems(database, lookups).Select(master => new UserItemRuntime(master)));
        }

        // 各行を1件ずつ変換してリスト化する。1行が例外を投げてもログを出して残りを続け、
        // 読み込み全体が途中で止まらないようにする。build が null を返した行はスキップする。
        private static List<T> BuildEachRow<T>(MiniSqlite database, string table, Func<MiniSqlite.Row, T> build) where T : class
        {
            var output = new List<T>();
            if (!database.HasTable(table)) {
                return output;
            }
            foreach (var row in database.SelectAll(table)) {
                try {
                    var built = build(row);
                    if (built != null) {
                        output.Add(built);
                    }
                }
                catch (Exception exception) {
                    Debug.LogError(table + " の行の読み込みに失敗したためスキップします (rowid=" + row.m_RowId + ")\n" + exception);
                }
            }
            return output;
        }

        // ---- 参照表 ----

        private static LookupTables BuildLookups(MiniSqlite database, IEnumerable<ShopMaster> shops)
        {
            var lookups = new LookupTables();
            foreach (var shop in shops) {
                if (!lookups.r_ShopsById.ContainsKey(shop.r_Id)) {
                    lookups.r_ShopsById[shop.r_Id] = shop;
                }
            }
            foreach (var row in database.SelectAll("sub_categories")) {
                var subCategoryId = new SubCategoryId(row.m_RowId);
                lookups.r_SubCategoryNames[subCategoryId] = row.GetString("name") ?? "";
                lookups.r_SubCategoryParents[subCategoryId] = new ParentCategoryId(row.GetLong("parent_category_id", 0));
            }
            foreach (var row in database.SelectAll("parent_categories")) {
                lookups.r_ParentCategoryNames[new ParentCategoryId(row.m_RowId)] = row.GetString("name") ?? "";
            }
            lookups.m_TagsByItem = BuildTagMap(database);
            lookups.m_VariationsByItem = BuildVariationMap(database);
            lookups.m_RegisteredAtByItem = BuildItemDateMap(database, "registered_items", "created_at");
            lookups.m_LibraryUpdatedAtByItem = BuildItemDateMap(database, "booth_item_update_history", "last_updated_at");
            return lookups;
        }

        private static Dictionary<ItemId, List<string>> BuildTagMap(MiniSqlite database)
        {
            return database.SelectAll("booth_item_tag_relations")
                .Select(row => (ItemId: row.GetLong("booth_item_id", 0), Tag: row.GetString("tag")))
                .Where(relation => relation.ItemId != 0 && !string.IsNullOrEmpty(relation.Tag))
                .GroupBy(relation => relation.ItemId)
                .ToDictionary(group => new ItemId(group.Key), group => group.Select(relation => relation.Tag).ToList());
        }

        // booth_item_variations をアイテム単位のバリエーション一覧にまとめる
        private static Dictionary<ItemId, List<ItemVariationMaster>> BuildVariationMap(MiniSqlite database)
        {
            return database.SelectAll("booth_item_variations")
                .Select(row => (
                    ItemId: row.GetLong("booth_item_id", 0),
                    Variation: new ItemVariationMaster(new VariationId(row.m_RowId), new OrderId(row.GetLong("order_id", 0)), row.GetString("variation_name") ?? "")))
                .Where(entry => entry.ItemId != 0)
                .GroupBy(entry => entry.ItemId)
                .ToDictionary(group => new ItemId(group.Key), group => group.Select(entry => entry.Variation).ToList());
        }

        // 指定テーブルの booth_item_id -> 指定日時列 をアイテム単位にまとめる
        private static Dictionary<ItemId, string> BuildItemDateMap(MiniSqlite database, string table, string column)
        {
            var map = new Dictionary<ItemId, string>();
            foreach (var row in database.SelectAll(table)) {
                var itemId = row.GetLong("booth_item_id", 0);
                if (itemId != 0) {
                    map[new ItemId(itemId)] = row.GetString(column) ?? "";
                }
            }
            return map;
        }

        // ---- アイテム（Master / Runtime） ----

        private static ItemMaster BuildItemMaster(MiniSqlite.Row row, LookupTables lookups)
        {
            var itemId = new ItemId(row.m_RowId);
            var shopId = new ShopId(row.GetString("shop_subdomain") ?? "");

            var shopName = shopId.r_Value;
            var shopThumbnail = "";
            if (lookups.r_ShopsById.TryGetValue(shopId, out var shop)) {
                shopName = shop.r_Name;
                shopThumbnail = shop.r_ThumbnailUrl.r_Value;
            }

            var subCategoryId = new SubCategoryId(row.GetLong("sub_category", 0));
            var subCategoryName = "";
            if (lookups.r_SubCategoryNames.TryGetValue(subCategoryId, out var resolvedSubName)) {
                subCategoryName = resolvedSubName ?? "";
            }
            var parentCategoryId = new ParentCategoryId(0);
            var parentCategoryName = "";
            if (lookups.r_SubCategoryParents.TryGetValue(subCategoryId, out var resolvedParentId)) {
                parentCategoryId = resolvedParentId;
                if (lookups.r_ParentCategoryNames.TryGetValue(parentCategoryId, out var resolvedParentName)) {
                    parentCategoryName = resolvedParentName ?? "";
                }
            }

            var registeredAt = "";
            lookups.m_RegisteredAtByItem.TryGetValue(itemId, out registeredAt);
            var libraryUpdatedAt = "";
            lookups.m_LibraryUpdatedAtByItem.TryGetValue(itemId, out libraryUpdatedAt);

            lookups.m_TagsByItem.TryGetValue(itemId, out var tags);
            lookups.m_VariationsByItem.TryGetValue(itemId, out var variations);
            var orderIds = (variations ?? new List<ItemVariationMaster>())
                .Select(variation => variation.r_OrderId)
                .Where(orderId => orderId.IsValid)
                .Distinct();

            return new ItemMaster(
                id: itemId,
                name: row.GetString("name") ?? "",
                shopId: shopId,
                shopName: shopName,
                shopThumbnailUrl: shopThumbnail,
                subCategoryId: subCategoryId,
                subCategoryName: subCategoryName,
                parentCategoryId: parentCategoryId,
                parentCategoryName: parentCategoryName,
                adult: row.GetLong("adult", 0) != 0,
                description: row.GetString("description") ?? "",
                thumbnailUrl: row.GetString("thumbnail_url") ?? "",
                publishedAt: row.GetString("published_at") ?? "",
                updatedAt: row.GetString("updated_at") ?? "",
                libraryUpdatedAt: libraryUpdatedAt ?? "",
                registeredAt: registeredAt ?? "",
                tags: tags ?? Enumerable.Empty<string>(),
                orderIds: orderIds,
                variations: variations ?? Enumerable.Empty<ItemVariationMaster>());
        }

        // ライブラリ配下の b{id} フォルダを走査して Runtime を組み立てる
        private static ItemRuntime BuildItemRuntime(ItemMaster master, string libraryPath)
        {
            if (string.IsNullOrEmpty(libraryPath)) {
                return new ItemRuntime(master, "", false, null);
            }

            var folder = Path.Combine(libraryPath, "b" + master.r_Id.r_Value);
            var exists = false;
            try {
                exists = Directory.Exists(folder);
            }
            catch (Exception exception) {
                Debug.LogWarning("アイテムフォルダの確認に失敗しました: " + master.r_Name + " (" + master.r_Id + ")\n" + folder + "\n" + exception);
                return new ItemRuntime(master, folder, false, null);
            }
            if (!exists) {
                return new ItemRuntime(master, folder, false, null);
            }
            return new ItemRuntime(master, folder, true, ScanItemFiles(folder, master));
        }

        private static List<ItemFile> ScanItemFiles(string folder, ItemMaster master)
        {
            try {
                // .unitypackage を先頭に寄せて見やすくする
                return Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                    .Select(fullPath => new ItemFile(
                        fullPath,
                        fullPath.Substring(folder.Length).TrimStart('\\', '/'),
                        fullPath.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(file => file.r_IsUnityPackage)
                    .ThenBy(file => file.r_RelativePath, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception exception) {
                Debug.LogWarning("アイテムフォルダのファイル列挙に失敗しました: " + master.r_Name + " (" + master.r_Id + ")\n" + folder + "\n" + exception);
                return new List<ItemFile>();
            }
        }

        // ---- preferences ----

        private static Preferences ReadPreferences(MiniSqlite database)
        {
            var preferences = new Preferences();
            var rows = database.SelectAll("preferences");
            if (rows.Count == 0) {
                return preferences;
            }
            var row = rows[0];
            preferences.r_Theme = row.GetString("theme") ?? "";
            preferences.r_Language = row.GetString("language") ?? "";
            preferences.r_LibraryPath = ReadLibraryPathValue(row);
            return preferences;
        }

        private static string ReadLibraryPathValue(MiniSqlite.Row row)
        {
            var found = row.Columns.TryGetValue("item_directory_path", out var value);
            if (!found || value == null) {
                return "";
            }
            // BLOB(UTF-16LE) で格納されている。文字列で来た場合はそのまま使う。
            if (value is byte[] blob) {
                return Encoding.Unicode.GetString(blob);
            }
            return value.ToString();
        }

        // ---- その他テーブル ----

        private static List<ShopMaster> BuildShops(MiniSqlite database)
        {
            return BuildEachRow(database, "shops", row => {
                var subdomain = row.GetString("subdomain");
                if (string.IsNullOrEmpty(subdomain)) {
                    return null;
                }
                return new ShopMaster(subdomain, row.GetString("name") ?? subdomain, row.GetString("thumbnail_url") ?? "");
            });
        }

        // booth_tags（マスタのタグ一覧）
        private static List<string> BuildAllTags(MiniSqlite database)
        {
            return BuildEachRow(database, "booth_tags", row => {
                var name = row.GetString("name");
                if (string.IsNullOrEmpty(name)) {
                    return null;
                }
                return name;
            });
        }

        private static List<NotificationMaster> BuildNotifications(MiniSqlite database)
        {
            return BuildEachRow(database, "notifications", row => new NotificationMaster(
                new NotificationId(row.m_RowId),
                row.GetString("title") ?? "",
                row.GetString("content") ?? "",
                row.GetLong("read", 0) != 0,
                row.GetString("created_at") ?? ""));
        }

        private static List<ItemListMaster> BuildLists(MiniSqlite database)
        {
            // 先に list_items から所属アイテムIDを集める
            var itemIdsByList = new Dictionary<ItemListId, HashSet<ItemId>>();
            if (database.HasTable("list_items")) {
                foreach (var row in database.SelectAll("list_items")) {
                    var listId = new ItemListId(row.GetLong("list_id", 0));
                    var boothId = ParseRegisteredItemId(row.GetString("item_id") ?? "");
                    if (boothId.HasValue) {
                        if (!itemIdsByList.TryGetValue(listId, out var set)) {
                            set = new HashSet<ItemId>();
                            itemIdsByList[listId] = set;
                        }
                        set.Add(new ItemId(boothId.Value));
                    }
                }
            }

            return BuildEachRow(database, "lists", row => {
                itemIdsByList.TryGetValue(new ItemListId(row.m_RowId), out var itemIds);
                return new ItemListMaster(
                    new ItemListId(row.m_RowId),
                    row.GetString("title") ?? "",
                    row.GetString("description") ?? "",
                    row.GetString("created_at") ?? "",
                    row.GetString("updated_at") ?? "",
                    itemIds);
            });
        }

        // registered_items.id は "b" + booth_item_id 形式
        private static long? ParseRegisteredItemId(string registeredId)
        {
            if (string.IsNullOrEmpty(registeredId) || registeredId[0] != 'b') {
                return null;
            }
            var parsed = long.TryParse(registeredId.Substring(1), out var value);
            if (parsed) {
                return value;
            }
            return null;
        }

        // smart_lists 本体に、条件(smart_list_criteria)とタグ(smart_list_tags)を紐付ける
        private static List<SmartListMaster> BuildSmartLists(MiniSqlite database)
        {
            var output = new List<SmartListMaster>();
            if (!database.HasTable("smart_lists")) {
                return output;
            }

            var criteriaByList = new Dictionary<SmartListId, List<SmartListCriteriaMaster>>();
            if (database.HasTable("smart_list_criteria")) {
                foreach (var row in database.SelectAll("smart_list_criteria")) {
                    var smartListId = new SmartListId(row.GetLong("smart_list_id", 0));
                    if (!criteriaByList.TryGetValue(smartListId, out var list)) {
                        list = new List<SmartListCriteriaMaster>();
                        criteriaByList[smartListId] = list;
                    }
                    list.Add(new SmartListCriteriaMaster(
                        new ParentCategoryId(row.GetLong("category_id", 0)),
                        new SubCategoryId(row.GetLong("subcategory_id", 0)),
                        row.GetString("text") ?? "",
                        row.GetString("age_restriction") ?? ""));
                }
            }

            var tagsByList = new Dictionary<SmartListId, List<string>>();
            if (database.HasTable("smart_list_tags")) {
                foreach (var row in database.SelectAll("smart_list_tags")) {
                    var smartListId = new SmartListId(row.GetLong("smart_list_id", 0));
                    var tag = row.GetString("tag");
                    if (!string.IsNullOrEmpty(tag)) {
                        if (!tagsByList.TryGetValue(smartListId, out var list)) {
                            list = new List<string>();
                            tagsByList[smartListId] = list;
                        }
                        list.Add(tag);
                    }
                }
            }

            return BuildEachRow(database, "smart_lists", row => {
                var smartListId = new SmartListId(row.m_RowId);
                criteriaByList.TryGetValue(smartListId, out var criteria);
                tagsByList.TryGetValue(smartListId, out var tags);
                return new SmartListMaster(
                    new SmartListId(row.m_RowId),
                    row.GetString("title") ?? "",
                    row.GetString("description") ?? "",
                    row.GetString("created_at") ?? "",
                    row.GetString("updated_at") ?? "",
                    criteria,
                    tags);
            });
        }

        private static List<UserItemMaster> BuildUserItems(MiniSqlite database, LookupTables lookups)
        {
            return BuildEachRow(database, "user_item_info", row => {
                var subCategoryId = new SubCategoryId(row.GetLong("sub_category", 0));
                var subCategoryName = "";
                if (lookups.r_SubCategoryNames.TryGetValue(subCategoryId, out var resolved)) {
                    subCategoryName = resolved ?? "";
                }
                return new UserItemMaster(
                    new UserItemId(row.m_RowId),
                    row.GetString("name") ?? "",
                    row.GetString("shop_name") ?? "",
                    row.GetString("thumbnail_filename") ?? "",
                    subCategoryId,
                    subCategoryName,
                    row.GetString("description") ?? "",
                    row.GetLong("adult", 0) != 0,
                    row.GetString("created_at") ?? "",
                    row.GetString("updated_at") ?? "");
            });
        }
    }
}
