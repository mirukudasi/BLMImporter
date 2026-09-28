using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// DBとライブラリフォルダから読み込んだ結果
    /// 各コレクションはID索引の辞書で持ち、ID検索を高速にする
    /// 列挙は挿入順
    /// </summary>
    public sealed class LibraryRuntimeSnapshot
    {
        /// <summary>
        /// data.db の配置場所
        /// </summary>
        public static string DefaultDatabasePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pm.booth.library-manager", "data.db");

        /// <summary>DBを読み込み、ライブラリフォルダを走査してスナップショットを構築する。</summary>
        public static LibraryRuntimeSnapshot Load(string databasePath)
        {
            return new LibraryRuntimeSnapshot(new LibraryMaster(new MiniSqlite(databasePath)));
        }

        // 実データの共有スナップショット。初回アクセスで読み込み、以後は使い回す
        // 常に実データ(DB)を読むため、プレビューのダミーはここを汚さない。
        public static LibraryRuntimeSnapshot Current => s_Current.Value;
        private static readonly Cache<LibraryRuntimeSnapshot> s_Current = new Cache<LibraryRuntimeSnapshot>(() => Load(DefaultDatabasePath));

        /// <summary>
        /// キャッシュ破棄
        /// 次回 Current アクセス時に実データを読み直させる
        /// </summary>
        public static void ClearCache() => s_Current.ClearCache();

        public readonly string r_LibraryPath;
        public readonly string r_Theme;
        public readonly string r_Language;
        public readonly IReadOnlyDictionary<ItemId, ItemRuntime> r_Items;
        public readonly IReadOnlyDictionary<ItemListId, ItemListRuntime> r_Lists;
        public readonly IReadOnlyDictionary<ShopId, ShopRuntime> r_Shops;
        public readonly IReadOnlyList<string> r_AllTags;
        public readonly IReadOnlyDictionary<NotificationId, NotificationRuntime> r_Notifications;
        public readonly IReadOnlyDictionary<SmartListId, SmartListRuntime> r_SmartLists;
        public readonly IReadOnlyDictionary<UserItemId, UserItemRuntime> r_UserItems;

        /// <summary>Master の内容から Runtime を組み立てる。アイテムはライブラリフォルダを走査して作る</summary>
        internal LibraryRuntimeSnapshot(LibraryMaster master)
        {
            var items = BuildItemRuntimes(master.r_Items.Values, master.r_Preference.r_LibraryPath);

            var preference = master.r_Preference;
            r_LibraryPath = preference.r_LibraryPath;
            r_Theme = preference.r_Theme;
            r_Language = preference.r_Language;
            r_Items = ToDictionary(items, item => item.Id);
            r_Lists = ToDictionary(master.r_Lists.Values.Select(list => new ItemListRuntime(list)), list => list.Id);
            r_Shops = ToDictionary(master.r_Shops.Values.Select(shop => new ShopRuntime(shop)), shop => shop.Id);
            r_AllTags = master.r_Tags.Select(tag => tag.r_Name).ToArray();
            r_Notifications = ToDictionary(master.r_Notifications.Values.Select(notification => new NotificationRuntime(notification)), notification => notification.Id);
            r_SmartLists = ToDictionary(master.r_SmartLists.Values.Select(smartList => new SmartListRuntime(smartList)), smartList => smartList.Id);
            r_UserItems = ToDictionary(master.r_UserItems.Values.Select(userItem => new UserItemRuntime(userItem)), userItem => userItem.Id);
        }

        // 挿入順を保ったまま辞書化する。同一IDが複数あれば最初の1件を採用する。
        private static Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(IEnumerable<TValue> source, Func<TValue, TKey> keySelector)
        {
            var dictionary = new Dictionary<TKey, TValue>();
            foreach (var value in source ?? Enumerable.Empty<TValue>()) {
                var key = keySelector(value);
                if (!dictionary.ContainsKey(key)) {
                    dictionary[key] = value;
                }
            }
            return dictionary;
        }

        // 1件でも壊れたアイテムがあっても全体の読み込みが止まらないよう、行ごとに安全に組み立てる
        private static List<ItemRuntime> BuildItemRuntimes(IEnumerable<ItemMaster> masters, string libraryPath)
        {
            var output = new List<ItemRuntime>();
            foreach (var master in masters) {
                try {
                    output.Add(BuildItemRuntime(master, libraryPath));
                }
                catch (Exception exception) {
                    Debug.LogError($"booth_items の行の読み込みに失敗したためスキップします (rowid={master.r_Id.r_Value})\n{exception}");
                }
            }
            return output;
        }

        // ライブラリ配下の b{id} フォルダを走査して Runtime を組み立てる
        private static ItemRuntime BuildItemRuntime(ItemMaster master, string libraryPath)
        {
            if (string.IsNullOrEmpty(libraryPath)) {
                return new ItemRuntime(master, "", false, null);
            }

            var folder = Path.Combine(libraryPath, $"b{master.r_Id.r_Value}");
            var exists = false;
            try {
                exists = Directory.Exists(folder);
            }
            catch (Exception exception) {
                Debug.LogWarning($"アイテムフォルダの確認に失敗しました: {master.r_Name} ({master.r_Id})\n{folder}\n{exception}");
                return new ItemRuntime(master, folder, false, null);
            }
            if (!exists) {
                return new ItemRuntime(master, folder, false, null);
            }
            return new ItemRuntime(master, folder, true, ScanItemFiles(folder, master));
        }

        // 一覧は受け取った ItemRuntime が配列に確定するので、ここでは確定させずに返す
        private static IEnumerable<ItemFile> ScanItemFiles(string folder, ItemMaster master)
        {
            try {
                // .unitypackage を先頭に寄せて見やすくする
                return Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                    .Select(fullPath => new ItemFile(fullPath, folder))
                    .OrderByDescending(file => file.r_IsUnityPackage)
                    .ThenBy(file => file.r_RelativePath, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception exception) {
                Debug.LogWarning($"アイテムフォルダのファイル列挙に失敗しました: {master.r_Name} ({master.r_Id})\n{folder}\n{exception}");
                return Enumerable.Empty<ItemFile>();
            }
        }
    }

    /// <summary>ライブラリ全体を横断する読み取りクエリ（UI 文言は含めない純データ集計）。</summary>
    public static class LibraryRuntimeSnapshotExtensions
    {
        /// <summary>IDからアイテムを引く。未登録・null は null を返す。</summary>
        public static ItemRuntime FindItem(this LibraryRuntimeSnapshot snapshot, ItemId itemId)
        {
            if (itemId == null) {
                return null;
            }
            snapshot.r_Items.TryGetValue(itemId, out var item);
            return item;
        }

        /// <summary>このリストに属するアイテムを、スナップショット全体から絞り込んで返す。</summary>
        public static IEnumerable<ItemRuntime> GetItemRuntimes(this LibraryRuntimeSnapshot snapshot, IItemList list)
        {
            return list.GetItemRuntimes(snapshot.r_Items.Values);
        }

        /// <summary>登録アイテムが持つサブカテゴリ名の一覧（重複除去・名前順）。</summary>
        public static List<string> SubCategoryNames(this LibraryRuntimeSnapshot snapshot)
        {
            return snapshot.r_Items.Values
                .Select(item => item.r_Master.r_SubCategoryName)
                .Where(name => !string.IsNullOrEmpty(name))
                .Distinct()
                .OrderBy(name => name)
                .ToList();
        }

        /// <summary>検索文字に部分一致するタグ候補を出現回数の多い順に返す。除外タグは候補から外す。</summary>
        public static List<string> SuggestTags(this LibraryRuntimeSnapshot snapshot, string query, ICollection<string> excludedTags, int limit = 20)
        {
            var keyword = (query ?? "").Trim().ToLowerInvariant();
            if (keyword.Length == 0) {
                return new List<string>();
            }
            var exclude = excludedTags ?? Array.Empty<string>();
            return snapshot.r_Items.Values
                .SelectMany(item => item.r_Master.r_Tags)
                .Where(tag => tag.ToLowerInvariant().Contains(keyword) && !exclude.Contains(tag))
                .GroupBy(tag => tag)
                .OrderByDescending(group => group.Count())
                .Select(group => group.Key)
                .Take(limit)
                .ToList();
        }
    }
}
