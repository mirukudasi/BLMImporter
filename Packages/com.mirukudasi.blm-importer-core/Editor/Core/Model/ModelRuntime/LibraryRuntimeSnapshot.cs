using System;
using System.Collections.Generic;
using System.Linq;

namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// DBとライブラリフォルダから読み込んだ結果
    /// 各コレクションはID索引の辞書で持ち、ID検索を高速にする
    /// 列挙は挿入順
    /// </summary>
    public sealed class LibraryRuntimeSnapshot
    {
        // 実データの共有スナップショット。初回アクセスで読み込み、以後は使い回す
        // 常に実データ(DB)を読むため、プレビューのダミーはここを汚さない。
        public static LibraryRuntimeSnapshot Current => s_Current.Value;
        private static readonly Cache<LibraryRuntimeSnapshot> s_Current = new Cache<LibraryRuntimeSnapshot>(() => LibraryData.LoadRuntime(LibraryData.DefaultDatabasePath));

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

        public LibraryRuntimeSnapshot(
            string libraryPath, string theme, string language, IEnumerable<ItemRuntime> items, 
            IEnumerable<ItemListRuntime> lists, IEnumerable<ShopRuntime> shops, IEnumerable<string> allTags, 
            IEnumerable<NotificationRuntime> notifications, IEnumerable<SmartListRuntime> smartLists, IEnumerable<UserItemRuntime> userItems)
        {
            r_LibraryPath = libraryPath ?? "";
            r_Theme = theme ?? "";
            r_Language = language ?? "";
            r_Items = ToDictionary(items, item => item.Id);
            r_Lists = ToDictionary(lists, list => list.Id);
            r_Shops = ToDictionary(shops, shop => shop.Id);
            r_AllTags = (allTags ?? Enumerable.Empty<string>()).ToArray();
            r_Notifications = ToDictionary(notifications, notification => notification.Id);
            r_SmartLists = ToDictionary(smartLists, smartList => smartList.Id);
            r_UserItems = ToDictionary(userItems, userItem => userItem.Id);
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
